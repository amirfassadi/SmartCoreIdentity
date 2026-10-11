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
env.update(ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS='http://127.0.0.1:5158',
 Identity__MacKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__MaterialKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__AccessSigningKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__BffClientKey=secrets.token_urlsafe(32),Identity__AuthenticationEnabled='true',
 Identity__DevInboxKey=secrets.token_urlsafe(32),Identity__WorkerEnabled='true',
 Identity__PasswordChangeEnabled='true',Identity__AuthOperatorKey=secrets.token_urlsafe(32),
 Identity__ApplicationPort='5158',Identity__AuthOperatorSocket=operator_socket)
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
 request=urllib.request.Request('http://127.0.0.1:5158'+path,
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
  email=secrets.token_hex(10)+'@example.test';password='safe HTTP authentication password';binding=secrets.token_urlsafe(32)
  status,start=call('/auth/register',dict(email=email,password=password,displayName='Test',bindingSecret=binding),{'Idempotency-Key':secrets.token_urlsafe(32)})
  assert status==202
  status,inbox=call('/dev/inbox/'+start['verificationSessionId'],headers={'X-Dev-Inbox-Key':env['Identity__DevInboxKey']});assert status==200
  verification=dict(verificationSessionId=start['verificationSessionId'],code=inbox['code'],bindingSecret=binding)
  assert call('/auth/register/verify',verification)[0]==201
  # The initial Credential and acknowledgment complete before password admission.
  for _ in range(3):
   time.sleep(1)
   status,ready=call('/auth/register/verify',verification)
   if status==200 and ready['status']=='Ready':break
  else:raise RuntimeError('Registration did not become Ready within proof budget')
  login=dict(email=email,password=password)
  status,result=call('/auth/login',login,bff);assert status==200
  session=result['session'];auth={**bff,'Authorization':'Bearer '+session['accessToken']}
  import uuid
  operation=str(uuid.uuid4());new_password='replacement HTTP password for tests'
  change=dict(operationId=operation,currentPassword=password,newPassword=new_password)
  assert call('/auth/password/change',change,bff)[0]==401
  assert call('/auth/password/change',{**change,'newPassword':'short'},auth)[0]==400
  assert call('/auth/password/change',{**change,'newPassword':None},auth)[0]==400
  assert call('/auth/password/change',{**change,'verified':True},auth)[0]==400
  assert call('/me',headers=auth)[0]==200
  status,accepted=call('/auth/password/change',change,auth)
  assert status==202 and accepted['operationId']==operation and accepted['stage']=='Fenced'
  recovery='/dev/auth/recovery/'+operation
  assert call(recovery,headers=bff)[0]==404
  assert call(recovery,{'action':'Retry'},bff)[0]==404
  operator={'X-Auth-Operator-Key':env['Identity__AuthOperatorKey']}
  assert call(recovery,headers=operator)[0]==404 # Network listener refuses even the valid operator key.
  assert call(recovery,headers=bff,private=True)[0]==404
  assert call(recovery,{'action':'ForceUnlock'},operator,private=True)[0]==400
  for _ in range(3):
   time.sleep(1)
   status,current=call(recovery,headers=operator,private=True)
   assert status==200 and current['operationId']==operation
   if current['stage']=='Reconciled':break
  else:raise RuntimeError('Password operation did not reconcile')
  assert call('/me',headers=auth)[0]==401
  assert call('/auth/refresh',{'refreshToken':session['refreshToken'],'foreground':True},bff)[0]==401
  assert call('/auth/login',login,bff)[0]==401
  assert call('/auth/login',dict(email=email,password=new_password),bff)[0]==200
  status,replayed=call('/auth/password/change',change,auth)
  assert status==202 and replayed['stage']=='Reconciled'
  print('HTTP PASSWORD PASS: opt-in change, strict proof/input, policy before fence, scheduled reconciliation, old Session denial and separately authenticated operator status')
 finally:
  process.terminate()
  try:process.wait(timeout=10)
  except subprocess.TimeoutExpired:process.kill()
  shutil.rmtree(operator_directory)
