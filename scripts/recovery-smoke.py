"""Opt-in authenticated BFF adapter HTTP checks against disposable test accounts."""
import base64,json,os,secrets,subprocess,time,urllib.request,urllib.error,socket,http.client,tempfile,shutil
from pathlib import Path
import yaml
from jsonschema import Draft202012Validator,FormatChecker
root=Path(__file__).resolve().parents[1]
env=os.environ.copy()
operator_directory=tempfile.mkdtemp(prefix='smartcore-operator-')
operator_socket=operator_directory+'/operator.sock'
assert env.get('ConnectionStrings__Identity'), 'Provide a disposable test database'
env.update(ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS='http://127.0.0.1:5159',
 Identity__MacKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__MaterialKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__AccessSigningKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__BffClientKey=secrets.token_urlsafe(32),Identity__AuthenticationEnabled='true',
 Identity__DevInboxKey=secrets.token_urlsafe(32),Identity__WorkerEnabled='true',
 Identity__PasswordChangeEnabled='true',Identity__RecoveryEnrollmentEnabled='true',Identity__AuthOperatorKey=secrets.token_urlsafe(32),
 Identity__ApplicationPort='5159',Identity__AuthOperatorSocket=operator_socket)
dotnet=env.get('SMARTCORE_DOTNET','dotnet');project=str(root/'src/SmartCore.Identity.Api')
subprocess.run([dotnet,'run','--project',project,'--no-build','--','--migrate'],env=env,check=True)
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
contract=yaml.safe_load((root/'contracts/authentication.openapi.yaml').read_text())
bff={'X-Bff-Client-Key':env['Identity__BffClientKey'],'X-Bff-Subject':secrets.token_urlsafe(32)}
class UnixConnection(http.client.HTTPConnection):
 def connect(self):
  self.sock=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM)
  self.sock.settimeout(30);self.sock.connect(operator_socket)
def call(path,body=None,headers=None,private=False):
 method='get' if body is None else 'post'
 request=urllib.request.Request('http://127.0.0.1:5159'+path,
  data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json',**(headers or {})})
 if private:
  connection=UnixConnection('localhost',timeout=30)
  connection.request(method.upper(),path,body=None if body is None else json.dumps(body),headers={'Content-Type':'application/json',**(headers or {})})
  r=connection.getresponse()
 else:
  try:r=opener.open(request,timeout=30)
  except urllib.error.HTTPError as e:r=e
 data=r.read();result=json.loads(data) if data else None
 contract_path='/dev/auth/recovery/{id}' if path.startswith('/dev/auth/recovery/') else path
 if contract_path in contract['paths']:
  definition=contract['paths'][contract_path][method]['responses'][str(r.status)]
  if 'content' in definition:
   schema=definition['content']['application/json']['schema']
   Draft202012Validator({**schema,'components':contract['components']},format_checker=FormatChecker()).validate(result)
  else:assert not data
  assert r.headers['Cache-Control']=='no-store'
 return r.status,result
with subprocess.Popen([dotnet,'run','--project',project,'--no-build'],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL) as process:
 try:
  for _ in range(200):
   try:
    if call('/health/ready')[0]==200:break
   except OSError:pass
   time.sleep(.1)
  else:raise RuntimeError('API did not become ready')
  email=secrets.token_hex(10)+'@example.test';password='safe recovery enrollment HTTP password';binding=secrets.token_urlsafe(32)
  status,start=call('/auth/register',dict(email=email,password=password,displayName='Recovery',bindingSecret=binding),{'Idempotency-Key':secrets.token_urlsafe(32)})
  assert status==202
  status,inbox=call('/dev/inbox/'+start['verificationSessionId'],headers={'X-Dev-Inbox-Key':env['Identity__DevInboxKey']});assert status==200
  verification=dict(verificationSessionId=start['verificationSessionId'],code=inbox['code'],bindingSecret=binding)
  assert call('/auth/register/verify',verification)[0]==201
  for _ in range(5):
   time.sleep(1)
   status,ready=call('/auth/register/verify',verification)
   if status==200 and ready['status']=='Ready':break
  else:raise RuntimeError('Registration did not become Ready')
  status,login=call('/auth/login',dict(email=email,password=password),bff)
  assert status==200 and login['person']['recoveryEnrollmentRequired'] is True
  import uuid
  operation=str(uuid.uuid4());request=dict(operationId=operation,currentPassword=password,acceptLossRisk=True)
  auth={**bff,'Authorization':'Bearer '+login['session']['accessToken']}
  assert call('/auth/recovery/enroll',request)[0]==401
  assert call('/auth/recovery/enroll',request,bff)[0]==401
  assert call('/auth/recovery/enroll',{**request,'acceptLossRisk':False},auth)[0]==400
  status,enrolled=call('/auth/recovery/enroll',request,auth)
  assert status==200 and enrolled['status']=='Issued' and len(enrolled['recoveryCode'])==32 and enrolled['version']==1
  status,replay=call('/auth/recovery/enroll',request,auth)
  assert status==200 and replay['status']=='AlreadyIssued' and 'recoveryCode' not in replay
  assert call('/me',headers=auth)[1]['recoveryEnrollmentRequired'] is False
  status,replacement=call('/auth/recovery/enroll',{**request,'operationId':str(uuid.uuid4())},auth)
  assert status==200 and replacement['version']==2 and replacement['recoveryCode']!=enrolled['recoveryCode']
  reset_bff={**bff,'X-Bff-Subject':secrets.token_urlsafe(32)}
  reset=dict(operationId=str(uuid.uuid4()),email=email,bindingSecret=secrets.token_urlsafe(32))
  status,pending=call('/auth/password/reset',reset,reset_bff);assert status==202
  assert call('/auth/password/reset',reset,reset_bff)==(202,pending)
  assert call('/dev/reset-inbox/'+pending['challengeId'],headers=bff)[0]==404
  status,otp=call('/dev/reset-inbox/'+pending['challengeId'],headers={'X-Dev-Inbox-Key':env['Identity__DevInboxKey']})
  assert status==200 and len(otp['code'])==8
  status,unknown=call('/auth/password/reset',{**reset,'operationId':str(uuid.uuid4()),'email':secrets.token_hex(10)+'@example.test'},reset_bff)
  assert status==202 and set(unknown)==set(pending) and unknown['status']==pending['status']
  assert call('/dev/reset-inbox/'+unknown['challengeId'],headers={'X-Dev-Inbox-Key':env['Identity__DevInboxKey']})[0]==404
  timings={'known':[],'unknown':[],'suppressed':[]}
  def measured(body,label):
   headers={**reset_bff,'X-Bff-Subject':secrets.token_urlsafe(32)}
   before=time.perf_counter();status,value=call('/auth/password/reset',body,headers)
   timings[label].append((time.perf_counter()-before)*1000);assert status==202
   return value
  for _ in range(2):measured({**reset,'operationId':str(uuid.uuid4())},'known')
  for _ in range(3):
   suppressed=measured({**reset,'operationId':str(uuid.uuid4())},'suppressed')
   measured({**reset,'operationId':str(uuid.uuid4()),'email':secrets.token_hex(10)+'@example.test'},'unknown')
  print('RESET TIMING OBSERVATION (small CI sample, not constant-time certification): '+json.dumps({
   label:{'n':len(values),'median_ms':round(__import__('statistics').median(values),2),'max_ms':round(max(values),2)}
   for label,values in timings.items()}))
  assert call('/me',headers=auth)[0]==200
  complete_bff={**bff,'X-Bff-Subject':secrets.token_urlsafe(32)}
  new_password='new password dual factor HTTP fixture'
  complete=dict(operationId=str(uuid.uuid4()),challengeId=pending['challengeId'],bindingSecret=reset['bindingSecret'],
   code=otp['code'],recoveryCode='  '+replacement['recoveryCode'].lower()+'  ',newPassword=new_password)
  assert call('/auth/password/reset/complete',complete)[0]==401
  wrong={**complete,'recoveryCode':'0'*32}
  assert call('/auth/password/reset/complete',wrong,complete_bff)[0]==401
  assert call('/me',headers=auth)[0]==200
  status,accepted=call('/auth/password/reset/complete',complete,complete_bff)
  assert status==202 and accepted['operationId']==complete['operationId']
  assert call('/me',headers=auth)[0]==401
  operator_headers={'X-Auth-Operator-Key':env['Identity__AuthOperatorKey']}
  for _ in range(100):
   status,state=call('/dev/auth/recovery/'+complete['operationId'],headers=operator_headers,private=True)
   if status==200 and state['stage']=='Reconciled':break
   time.sleep(.1)
  else:raise RuntimeError('Reset did not reconcile')
  status,replayed=call('/auth/password/reset/complete',complete,complete_bff)
  assert status==202 and replayed['stage']=='Reconciled'
  assert call('/auth/login',dict(email=email,password=password),bff)[0]==401
  status,new_login=call('/auth/login',dict(email=email,password=new_password),bff)
  assert status==200 and new_login['person']['recoveryEnrollmentRequired'] is True
  new_auth={**bff,'X-Bff-Subject':secrets.token_urlsafe(32),'Authorization':'Bearer '+new_login['session']['accessToken']}
  status,new_factor=call('/auth/recovery/enroll',dict(operationId=str(uuid.uuid4()),currentPassword=new_password,acceptLossRisk=True),new_auth)
  assert status==200 and new_factor['version']==3 and len(new_factor['recoveryCode'])==32
  assert call('/me',headers=new_auth)[1]['recoveryEnrollmentRequired'] is False
  failure_timings={'real':[],'decoy':[],'suppressed':[]}
  for _ in range(12):
   for label,ch in [('real',pending),('decoy',unknown),('suppressed',suppressed)]:
    before=time.perf_counter()
    payload={**complete,'challengeId':ch['challengeId'],'recoveryCode':'0'*32}
    headers={**bff,'X-Bff-Subject':secrets.token_urlsafe(32)}
    assert call('/auth/password/reset/complete',payload,headers)[0]==401
    failure_timings[label].append((time.perf_counter()-before)*1000)
  def percentile(values,fraction):return sorted(values)[__import__('math').ceil(len(values)*fraction)-1]
  summaries={label:{'n':len(values),'p50_ms':round(percentile(values,.5),2),'p90_ms':round(percentile(values,.9),2),'max_ms':round(max(values),2)}
   for label,values in failure_timings.items()}
  assert all(min(values)>=190 and max(values)<350 for values in failure_timings.values()),summaries
  assert max(v['p50_ms'] for v in summaries.values())-min(v['p50_ms'] for v in summaries.values())<35,summaries
  assert max(v['p90_ms'] for v in summaries.values())-min(v['p90_ms'] for v in summaries.values())<60,summaries
  print('RESET TIMING HTTP COMPLETION PASS (12/group, controlled CI, not load certification): '+json.dumps(summaries))
  print('HTTP RECOVERY PASS: one-time enrollment, uniform initiation, two-factor reset, receipt recovery, old Session denial, bounded replay, fresh-login replacement and pinned response schemas')
 finally:
  process.terminate()
  try:process.wait(timeout=10)
  except subprocess.TimeoutExpired:process.kill();process.wait()
  shutil.rmtree(operator_directory)
