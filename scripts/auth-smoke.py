"""Opt-in authenticated BFF adapter HTTP checks against disposable test accounts."""
import base64,json,os,secrets,subprocess,time,urllib.request,urllib.error
from pathlib import Path
import yaml
from jsonschema import Draft202012Validator,FormatChecker
root=Path(__file__).resolve().parents[1]
env=os.environ.copy()
assert env.get('ConnectionStrings__Identity'), 'Provide a disposable test database'
env.update(ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS='http://127.0.0.1:5157',
 Identity__MacKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__MaterialKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__AccessSigningKey=base64.b64encode(secrets.token_bytes(32)).decode(),
 Identity__BffClientKey=secrets.token_urlsafe(32),Identity__AuthenticationEnabled='true',
 Identity__DevInboxKey=secrets.token_urlsafe(32),Identity__WorkerEnabled='true')
dotnet=env.get('SMARTCORE_DOTNET','dotnet');project=str(root/'src/SmartCore.Identity.Api')
subprocess.run([dotnet,'run','--project',project,'--no-build','--','--migrate'],env=env,check=True)
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
contract=yaml.safe_load((root/'contracts/authentication.openapi.yaml').read_text())
bff={'X-Bff-Client-Key':env['Identity__BffClientKey']}
def call(path,body=None,headers=None):
 method='get' if body is None else 'post'
 request=urllib.request.Request('http://127.0.0.1:5157'+path,
  data=None if body is None else json.dumps(body).encode(),headers={'Content-Type':'application/json',**(headers or {})})
 try:r=opener.open(request,timeout=30)
 except urllib.error.HTTPError as e:r=e
 data=r.read();result=json.loads(data) if data else None
 if path in contract['paths']:
  definition=contract['paths'][path][method]['responses'][str(r.status)]
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
  login=dict(email=email,password=password)
  assert call('/auth/login',login)[0]==401
  assert call('/auth/login',{**login,'password':None},bff)[0]==400
  assert call('/auth/login',{**login,'extra':True},bff)[0]==400
  # Await Ready through bounded authorized proof replay, not by inspecting Credential state.
  for _ in range(3):
   time.sleep(1)
   status,ready=call('/auth/register/verify',verification)
   if status==200 and ready['status']=='Ready':break
  else:raise RuntimeError('Registration did not become Ready within proof budget')
  status,result=call('/auth/login',login,bff);assert status==200
  session=result['session'];auth={**bff,'Authorization':'Bearer '+session['accessToken']}
  assert call('/me',headers=auth)[0]==200
  assert call('/me',headers=bff)[0]==401
  assert call('/auth/refresh',{'refreshToken':session['refreshToken']},bff)[0]==400
  assert call('/auth/refresh',{'refreshToken':session['refreshToken'],'foreground':True}, {'X-Bff-Client-Key':'wrong-client'})[0]==401
  status,rotated=call('/auth/refresh',{'refreshToken':session['refreshToken'],'foreground':True},bff)
  assert status==200 and rotated['expiresAt']==session['expiresAt'] and rotated['refreshToken']!=session['refreshToken']
  assert call('/auth/refresh',{'refreshToken':session['refreshToken'],'foreground':True},bff)[0]==401
  assert call('/auth/refresh',{'refreshToken':rotated['refreshToken'],'foreground':True},bff)[0]==401
  assert call('/me',headers=auth)[0]==401
  status,result=call('/auth/login',login,bff);assert status==200
  session=result['session'];auth={**bff,'Authorization':'Bearer '+session['accessToken']}
  assert call('/auth/logout',{'sessionId':session['sessionId']},auth)[0]==204
  assert call('/auth/logout',{'sessionId':session['sessionId']},auth)[0]==204
  assert call('/me',headers=auth)[0]==401
  print('HTTP AUTH PASS: BFF admission, strict input, Ready login/self, rotation, zero-grace reuse and bound logout replay')
 finally:
  process.terminate()
  try:process.wait(timeout=10)
  except subprocess.TimeoutExpired:process.kill()
