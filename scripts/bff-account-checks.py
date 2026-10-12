"""Real Identity+BFF acceptance through TLS ingress. Captures all proof material privately."""
import concurrent.futures
from http.cookies import SimpleCookie
import json
import os
import secrets
import subprocess
import time
import urllib.error
import urllib.request


def account_checks(root, tls, private, env, identity_env, dotnet, validate_response, contract, identity_process):
    origin='https://localhost:7443'
    def call(opener, address, path, body=None, headers=None, method=None):
        request=urllib.request.Request(address+path,data=None if body is None else json.dumps(body).encode(),
            method=method or ('GET' if body is None else 'POST'),headers={'Content-Type':'application/json',**(headers or {})})
        try:response=opener.open(request,timeout=30)
        except urllib.error.HTTPError as error:response=error
        raw=response.read();data=json.loads(raw) if raw else None
        if address==origin:
            validate_response(contract,path,request.method,response.status,data,{key:response.headers.get_all(key) for key in response.headers.keys()})
            assert not raw or not any(key.encode() in raw for key in ('accessToken','refreshToken','personId','sessionId','generation'))
        return response.status,data,response.headers
    def trusted(path,body=None,headers=None):return call(private,'http://127.0.0.1:5157',path,body,headers)
    # Seed exclusively via the existing private registration/inbox fixture, outside browser origin.
    email=secrets.token_hex(12)+'@example.test';password='private account HTTP fixture password';binding=secrets.token_urlsafe(32)
    _,start,_=trusted('/auth/register',{'email':email,'password':password,'displayName':'B1 account fixture','bindingSecret':binding},{'Idempotency-Key':secrets.token_urlsafe(32)})
    status,inbox,_=trusted('/dev/inbox/'+start['verificationSessionId'],headers={'X-Dev-Inbox-Key':identity_env['Identity__DevInboxKey']});assert status==200
    verification={'verificationSessionId':start['verificationSessionId'],'code':inbox['code'],'bindingSecret':binding}
    assert trusted('/auth/register/verify',verification)[0]==201
    for _ in range(15):
        status,ready,_=trusted('/auth/register/verify',verification)
        if status==200 and ready['status']=='Ready':break
        time.sleep(.2)
    else:raise AssertionError('Private seed did not reach Ready')
    class Browser:
        def __init__(self):self.cookies={};self.token=None
        def request(self,path,body=None,overrides=None):
            headers={'Origin':origin,'Sec-Fetch-Site':'same-origin','Cookie':'; '.join(key+'='+value for key,value in self.cookies.items())}
            if self.token:headers['X-CSRF-Token']=self.token
            headers.update(overrides or {})
            status,data,fields=call(tls,origin,path,body,headers)
            for field in fields.get_all('Set-Cookie',[]):
                parsed=SimpleCookie();parsed.load(field)
                for key,cookie in parsed.items():
                    assert cookie['secure'] and cookie['httponly'] and cookie['samesite'].lower()=='lax' and cookie['path']=='/' and not cookie['domain']
                    if cookie.value:self.cookies[key]=cookie.value
                    else:self.cookies.pop(key,None)
            return status,data,fields
        def csrf(self):
            status,data,_=self.request('/bff/csrf');assert status==200;self.token=data['requestToken']
    browser=Browser();browser.csrf();anon_token=browser.token;anon_cookies=browser.cookies.copy()
    login={'email':email,'password':password}
    assert browser.request('/bff/login',{**login,'password':'wrong password'})[0]==401
    assert '__Host-smartcore-session' not in browser.cookies
    status,data,_=browser.request('/bff/login',login);assert status==200 and data['person']['displayName']=='B1 account fixture'
    assert set(data)=={'person'} and '__Host-smartcore-antiforgery' not in browser.cookies
    session=browser.cookies['__Host-smartcore-session']
    browser.cookies.update(anon_cookies);browser.token=anon_token
    assert browser.request('/bff/me',{})[0]==403 # Pre-login CSRF rejected even if old cookie is restored.
    browser.csrf();auth_token=browser.token;auth_cookies=browser.cookies.copy()
    assert browser.request('/bff/me',{})[0]==200
    assert browser.request('/bff/me/action',{})[0]==200
    assert browser.request('/bff/login',login)[0]==409
    assert browser.request('/bff/me',{}, {'Origin':'https://foreign.invalid'})[0]==403
    fixture_assembly=root/'tests/SmartCore.Identity.Tests/bin/Release/net10.0/SmartCore.Identity.Tests.dll'
    def fixture(action,cookie=session):
        value={'Connection':env['ConnectionStrings__Bff'],'IdentityConnection':env['ConnectionStrings__Bff'],
            'MacKey':env['Bff__SessionMacKey'],'MaterialKey':env['Bff__SessionMaterialKey'],'Action':action,'Cookie':cookie}
        result=subprocess.run([dotnet,str(fixture_assembly),'--browser-account-fixture'],input=json.dumps(value)+'\n',text=True,capture_output=True,env={**os.environ,'SMARTCORE_TEST_ALLOW_RESET':'yes'},timeout=30)
        assert result.returncode==0,'Private account fixture failed'
        return json.loads(result.stdout.strip().splitlines()[-1])
    before=fixture('Inspect');fixture('Due')
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        statuses=list(pool.map(lambda _:browser.request('/bff/me',{})[0],range(4)))
    assert statuses==[200]*4
    after=fixture('Inspect');assert after['Generation']==before['Generation']+1 and after['Version']==before['Version']+1
    assert browser.cookies['__Host-smartcore-session']==session # Refresh preserves opaque cookie and CSRF identity.
    assert browser.request('/bff/me',{})[0]==200
    fixture('Revoke')
    assert browser.request('/bff/me',{})[0]==401
    assert '__Host-smartcore-session' not in browser.cookies and '__Host-smartcore-antiforgery' not in browser.cookies
    browser.csrf();assert browser.request('/bff/login',login)[0]==200;browser.csrf()
    # A second real login for the same owner rejects first session's antiforgery identity.
    current_cookies=browser.cookies.copy();browser.cookies['__Host-smartcore-antiforgery']=auth_cookies['__Host-smartcore-antiforgery'];browser.token=auth_token
    assert browser.request('/bff/me',{})[0]==403
    browser.cookies=current_cookies;browser.csrf()
    new_session=browser.cookies['__Host-smartcore-session']
    if os.environ.get('SMARTCORE_BROWSER_E2E')=='yes':
        browser_runner=root/'web/identity/e2e/account.mjs'
        child=subprocess.run(['node',str(browser_runner)],input=json.dumps({'email':email,'password':password,'tlsCertificatePath':env['SMARTCORE_ACCOUNT_TLS_CERT']})+'\n',text=True,capture_output=True,timeout=120)
        assert child.returncode==0, 'Real browser acceptance failed: '+child.stdout[-4000:]+child.stderr[-4000:]
        print(child.stdout.strip())
    # Upstream outage: /me preserves cookie; logout succeeds only after durable local intent.
    identity_process.terminate();identity_process.wait(timeout=10)
    assert browser.request('/bff/me',{})[0]==503
    assert browser.cookies['__Host-smartcore-session']==new_session
    assert browser.request('/bff/logout',{})[0]==204
    assert not browser.cookies
    state=fixture('Inspect',new_session);assert state['Status'] in ('LogoutRequested','Closed')
    browser.cookies={'__Host-smartcore-session':new_session};browser.csrf()
    assert browser.request('/bff/me',{})[0]==401
    # Restore private Identity; maintenance completes cookie-free durable logout.
    assembly=root/'src/SmartCore.Identity.Api/bin/Release/net10.0/SmartCore.Identity.Api.dll'
    recovered=subprocess.Popen([dotnet,str(assembly)],env=identity_env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    try:
        for _ in range(100):
            if fixture('Inspect',new_session)['Status']=='Closed':break
            time.sleep(.2)
        else:raise AssertionError('Cookie-free maintenance did not close durable logout')
    finally:recovered.terminate();recovered.wait(timeout=10)
    print('BFF ACCOUNT HTTP PASS: private seeded Ready account; safe login/profile DTO and cookie attributes; real CSRF rebinding across logins/hosts; four parallel reads cause exactly one Identity rotation; verified access rejection; outage preserves session; durable local logout and cookie-free maintenance complete.')
