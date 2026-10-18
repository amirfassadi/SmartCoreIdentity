"""Live guard checks invoked inside the private two-host/TLS disposable runner.
No fixture credentials, session cookies or CSRF request tokens are logged.
"""
import json
import os
import subprocess
import urllib.error
import urllib.request
import uuid
from http.cookies import SimpleCookie


def guard_checks(root, tls, env, dotnet, validate_response, contract, direct_tls):
    def request(path, body=None, headers=None, method='POST', address='https://localhost:7443'):
        data = body if isinstance(body, bytes) else None if body is None else json.dumps(body).encode()
        request = urllib.request.Request(address+path,data=data,method=method,headers={'Host':'localhost:7443','Content-Type':'application/json','Origin':'https://localhost:7443','Sec-Fetch-Site':'same-origin',**(headers or {})})
        try: response=tls.open(request,timeout=10)
        except urllib.error.HTTPError as error: response=error
        content=response.read(); result=json.loads(content) if content else None
        fields={name:response.headers.get_all(name) for name in response.headers.keys()}
        validate_response(contract,path,method,response.status,result,fields)
        assert not result or not any(name in result for name in ('personId','sessionId','accessToken','refreshToken'))
        return response.status,result,response.headers

    # First tests after wiring the guard: exact committed competing-error fixtures over real TLS.
    cases=json.loads((root/'tests/browser/b1-guard-precedence.json').read_text())
    for case in cases:
        raw=case['body'].encode();raw+=b' '*max(0,case['bodyBytes']-len(raw))
        status,result,_=request(case['path'],raw,case['headers'],case['method'])
        assert (status,result['code'])==(case['status'],case['code']),case['name']
    # Also exercise host/Kestrel size precedence directly over TLS, not only the ingress size check.
    first=cases[0];raw=first['body'].encode()+b' '*(first['bodyBytes']-1)
    status,result,_=request(first['path'],raw,first['headers'],first['method'],direct_tls)
    assert (status,result['code'])==(413,'REQUEST_TOO_LARGE')

    def bootstrap(session=None, antiforgery=None):
        cookies=[]
        if session:cookies.append('__Host-smartcore-session='+session)
        if antiforgery:cookies.append('__Host-smartcore-antiforgery='+antiforgery)
        status,result,headers=request('/bff/csrf',headers={'Cookie':'; '.join(cookies)} if cookies else {},method='GET')
        assert status==200
        for field in headers.get_all('Set-Cookie',[]):
            cookie=SimpleCookie();cookie.load(field)
            if '__Host-smartcore-antiforgery' in cookie:antiforgery=cookie['__Host-smartcore-antiforgery'].value
        assert antiforgery
        return result['requestToken'],antiforgery

    def proof(token,cookie,session=None):
        return {'X-CSRF-Token':token,'Cookie':'__Host-smartcore-antiforgery='+cookie+('' if not session else '; __Host-smartcore-session='+session)}

    token,af=bootstrap()
    login={'email':'fixture@example.test','password':'valid login fixture password'}
    assert request('/bff/login',login,proof(token,af))[0]==503 # Guard passed; actual login still disconnected.
    assert request('/bff/login',b'{',proof(token,af))[0]==400
    assert request('/bff/login',{'email':'fixture@example.test','password':'😀'*64},proof(token,af))[0]==503
    assert request('/bff/login',{'email':'fixture@example.test','password':'😀'*65},proof(token,af))[0]==400
    assert request('/bff/login',{'mobile':'۰۹۱۲۱۲۳۴۵۶۷','password':'valid login fixture password'},proof(token,af))[0]==503
    for wrong in [b'{"email":"a@example.test","password":"a","password":"b"}',json.dumps({**login,'personId':str(uuid.uuid4())}).encode(),b'null']:
        assert request('/bff/login',wrong,proof(token,af))[0]==400
    for path in ['/bff/me','/bff/me/action','/bff/logout']:
        assert request(path,{},proof(token,af))[0]==401 # Anonymous token cannot create an authenticated principal.
    assert request('/bff/login',login,proof('invalid',af))[0]==403
    assert request('/bff/login',login,{**proof(token,af),'Sec-Fetch-Site':'same-site'})[1]['code']=='ORIGIN_REJECTED'
    assert request('/bff/login',login,{**proof(token,af),'Origin':'null'})[1]['code']=='ORIGIN_REJECTED'

    fixture_assembly=root/'tests/SmartCore.Identity.Tests/bin/Release/net10.0/SmartCore.Identity.Tests.dll'
    identity=str(uuid.uuid4())
    def fixture(status,cookie=None):
        value={'Connection':env['ConnectionStrings__Bff'],'MacKey':env['Bff__SessionMacKey'],'MaterialKey':env['Bff__SessionMaterialKey'],'Status':status,'IdentitySession':identity,'Cookie':cookie}
        run=subprocess.run([dotnet,str(fixture_assembly),'--browser-session-fixture'],input=json.dumps(value)+'\n',text=True,capture_output=True,env={**os.environ,'SMARTCORE_TEST_ALLOW_RESET':'yes'},timeout=30)
        assert run.returncode==0,'Private disposable session fixture failed'
        return json.loads(run.stdout.strip().splitlines()[-1])

    a,b=fixture('Active'),fixture('Active') # Same underlying Identity session, different BFF session UUIDs.
    assert a['SessionId']!=b['SessionId']
    # Disposable database fault probe catches automatic/default authentication before guards.
    fixture('HideLookup')
    try:
        assert request('/bff/login',login,{**proof(token,af,a['Cookie']),'Content-Type':'text/plain'})[0]==415
        assert request('/bff/login',login,{**proof(token,af,a['Cookie']),'Origin':'https://foreign.invalid'})[1]['code']=='ORIGIN_REJECTED'
        assert request('/bff/me',{},proof(token,af,a['Cookie']))[0]==503 # DB lookup fails closed, never anonymous/401 fallback.
    finally:fixture('RestoreLookup')
    assert request('/bff/me',{},proof(token,af,a['Cookie']))[0]==403 # Pre-login token rejected after session appears.
    auth_token,auth_af=bootstrap(a['Cookie'],af)
    # Round-robin routes mint/validate across both real hosts using the shared protected ring.
    for _ in range(4):assert request('/bff/me',{},proof(auth_token,auth_af,a['Cookie']))[0]==503
    fixture('Advance',a['Cookie'])
    assert request('/bff/me',{},proof(auth_token,auth_af,a['Cookie']))[0]==503 # Stable identity across stored version change.
    assert request('/bff/login',login,proof(auth_token,auth_af,a['Cookie']))[0]==409
    assert request('/bff/me',{},proof(auth_token,auth_af,b['Cookie']))[0]==403 # Same owner does not reuse old-session CSRF.
    fixture('CloseExisting',a['Cookie'])
    assert request('/bff/me',{},proof(auth_token,auth_af,a['Cookie']))[0]==403
    anon_token,anon_af=bootstrap(a['Cookie'],auth_af)
    assert request('/bff/me',{},proof(anon_token,anon_af,a['Cookie']))[0]==401
    new_token,new_af=bootstrap(b['Cookie'])
    assert request('/bff/me',{},proof(new_token,new_af,b['Cookie']))[0]==503
    assert request('/bff/me',{},proof(auth_token,auth_af,b['Cookie']))[0]==403
    expired=fixture('Expired')
    expired_token,expired_af=bootstrap(expired['Cookie'])
    assert request('/bff/me',{},proof(expired_token,expired_af,expired['Cookie']))[0]==401
    logout=fixture('Active');logout_token,logout_af=bootstrap(logout['Cookie'])
    fixture('LogoutExisting',logout['Cookie'])
    assert request('/bff/me',{},proof(logout_token,logout_af,logout['Cookie']))[0]==403
    assert request('/bff/logout',{},proof(logout_token,logout_af,logout['Cookie']))[0]==503
    print('BFF LIVE GUARD PASS: five competing-error fixtures through real TLS ingress; host size precedence via direct TLS; shared-ring CSRF across two hosts; anonymous/session/new-session/closed/expired/logout binding; strict DTO/UTF16 checks. Synthetic private session fixtures, no browser login/logout/reset E2E claim.')
