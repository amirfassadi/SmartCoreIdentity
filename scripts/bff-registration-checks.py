"""Private B2 HTTP/Chromium acceptance. All proof/inbox/DB seams remain outside ingress."""
from http.cookies import SimpleCookie
import json
import os
import secrets
import subprocess
import urllib.error
import urllib.request


def registration_checks(root,tls,private,env,identity_env,dotnet,validate_response,contract,identity_process):
    origin='https://localhost:7443'
    def fixture(contact,action='Inspect'):
        value={'Connection':env['ConnectionStrings__Bff'],'MacKey':identity_env['Identity__MacKey'],
               'MaterialKey':identity_env['Identity__MaterialKey'],'Action':action,'Contact':contact}
        child=subprocess.run([dotnet,str(root/'tests/SmartCore.Identity.Tests/bin/Release/net10.0/SmartCore.Identity.Tests.dll'),'--browser-registration-fixture'],
            input=json.dumps(value)+'\n',capture_output=True,text=True,timeout=30,env={**os.environ,'SMARTCORE_TEST_ALLOW_RESET':'yes'})
        assert child.returncode==0,'Private registration fixture failed'
        return json.loads(child.stdout.strip().splitlines()[-1])
    def inbox(contact,setup=False):
        row=fixture(contact);path='/dev/setup-inbox/'+row['Setup'] if setup else '/dev/inbox/'+row['Verification']
        request=urllib.request.Request('http://127.0.0.1:5157'+path,headers={'X-Dev-Inbox-Key':identity_env['Identity__DevInboxKey']})
        with private.open(request,timeout=10) as response:return json.loads(response.read())['code']
    class Browser:
        def __init__(self):self.cookies={};self.token=None
        def request(self,path,body=None,overrides=None,raw=None):
            headers={'Content-Type':'application/json','Origin':origin,'Sec-Fetch-Site':'same-origin',
                'Cookie':'; '.join(k+'='+v for k,v in self.cookies.items())}
            if self.token:headers['X-CSRF-Token']=self.token
            headers.update(overrides or {})
            request=urllib.request.Request(origin+path,data=raw if raw is not None else None if body is None else json.dumps(body).encode(),
                headers=headers,method='GET' if body is None and raw is None else 'POST')
            try:response=tls.open(request,timeout=30)
            except urllib.error.HTTPError as failure:response=failure
            wire=response.read();data=json.loads(wire) if wire else None
            validate_response(contract,path,request.method,response.status,data,{k:response.headers.get_all(k) for k in response.headers.keys()})
            assert not any(k.encode() in wire for k in ('bindingSecret','verificationSessionId','setupChallengeId','registrationId','personId','accessToken','refreshToken'))
            for field in response.headers.get_all('Set-Cookie',[]):
                parsed=SimpleCookie();parsed.load(field)
                for key,cookie in parsed.items():
                    if cookie.value:self.cookies[key]=cookie.value
                    else:self.cookies.pop(key,None)
            return response.status,data
        def csrf(self):
            status,data=self.request('/bff/csrf');assert status==200;self.token=data['requestToken']
    def start(browser,contact,flow=None,password='private browser registration password'):
        body={'flowId':flow or secrets.token_urlsafe(32),'email':contact,'password':password,'displayName':'B2 fixture'}
        status,data=browser.request('/bff/register',body);assert status==202
        assert '__Host-smartcore-session' not in browser.cookies
        return body,data
    browser=Browser();browser.csrf();email=secrets.token_hex(12)+'@example.test'
    body,first=start(browser,email);code=inbox(email);before=fixture(email)
    assert browser.request('/bff/register',body)==(202,first) # Lost-response explicit replay, no lifetime extension.
    assert fixture(email)['Deliveries']==before['Deliveries']==1
    assert browser.request('/bff/register',{**body,'password':'different registration password'})[0]==409
    proof={'flowId':body['flowId'],'code':code}
    # Transport/schema failures cannot reach Identity or consume proof budget.
    assert browser.request('/bff/register/verify',proof,{'Origin':'https://foreign.invalid'})[0]==403
    assert browser.request('/bff/register/verify',proof,{'Content-Type':'text/plain'})[0]==415
    assert browser.request('/bff/register/verify',raw=b'x'*16385,overrides={'Content-Type':'text/plain'})[0]==413
    assert browser.request('/bff/register/verify',{**proof,'personId':'forbidden'})[0]==400
    assert browser.request('/bff/register/verify',{**proof,'flowId':secrets.token_urlsafe(32)})[1]['code']=='REGISTRATION_FLOW_EXPIRED'
    assert fixture(email)['Attempts']==0
    cookie=browser.cookies['__Host-smartcore-registration'];browser.cookies['__Host-smartcore-registration']='A'*64
    assert browser.request('/bff/register/verify',proof)[1]['code']=='REGISTRATION_FLOW_EXPIRED'
    browser.cookies['__Host-smartcore-registration']=cookie
    assert browser.request('/bff/register/resend',{'flowId':body['flowId']})[0]==202
    assert inbox(email)==code and fixture(email)['Deliveries']==1 # Cooldown remains authoritative.
    fixture(email,'AllowResend');assert browser.request('/bff/register/resend',{'flowId':body['flowId']})[0]==202
    replacement=inbox(email)
    # Random resend could coincidentally equal old code; wrong proof is deterministic.
    wrong='000000' if replacement!='000000' else '999999'
    assert browser.request('/bff/register/verify',{**proof,'code':wrong})[1]['code']=='VERIFICATION_FAILED'
    proof['code']=replacement
    assert browser.request('/bff/register/verify',proof)==(200,{'status':'PendingCredential'})
    assert '__Host-smartcore-session' not in browser.cookies
    fixture(email,'Provision');assert browser.request('/bff/register/verify',proof)==(200,{'status':'Ready'})
    # Existing contact has identical start shape; only proof can reveal sign-in/setup next action.
    peer=Browser();peer.csrf();existing,pending=start(peer,email)
    assert set(first)==set(pending) and first['status']==pending['status']
    assert peer.request('/bff/register/verify',{'flowId':existing['flowId'],'code':inbox(email)})==(200,{'status':'SignInRequired'})
    # Shared cookie changed in another tab must fail closed against the tab's correlation ID.
    browser.cookies['__Host-smartcore-registration']=peer.cookies['__Host-smartcore-registration']
    assert browser.request('/bff/register/verify',proof)[1]['code']=='REGISTRATION_FLOW_EXPIRED'
    # Contact hour budget returns the real 3600-second retry; transport windows remain 60.
    for _ in range(3):start(peer,email)
    limit={'flowId':secrets.token_urlsafe(32),'email':email,'password':body['password'],'displayName':'B2 fixture'}
    status,data=peer.request('/bff/register',limit);assert status==429 and data['retryAfterSeconds']==3600
    # Distinct setup code/candidate recovers a genuinely unfinished registration.
    setup_email=secrets.token_hex(12)+'@example.test';fixture(setup_email,'SeedPending')
    setup_browser=Browser();setup_browser.csrf();setup_body,_=start(setup_browser,setup_email)
    original=inbox(setup_email);setup_proof={'flowId':setup_body['flowId'],'code':original}
    assert setup_browser.request('/bff/register/verify',setup_proof)==(200,{'status':'SetupRequired'})
    status,setup_result=setup_browser.request('/bff/register/setup',setup_proof);assert status==202
    assert setup_browser.request('/bff/register/setup',setup_proof)==(202,setup_result)
    setup_code=inbox(setup_email,True);assert setup_code!=original
    completion={'flowId':setup_body['flowId'],'code':setup_code,'newPassword':'new browser setup password'}
    status,data=setup_browser.request('/bff/register/complete',completion)
    assert status==200 and data=={'status':'Ready','credentialOutcome':'CandidateSelected'}
    assert setup_browser.request('/bff/register/complete',completion)==(200,data)
    assert setup_browser.request('/bff/register/complete',{**completion,'newPassword':'changed browser setup password'})[0]==409
    assert '__Host-smartcore-session' not in setup_browser.cookies
    # A winner installed between setup and completion remains authoritative.
    winner_email=secrets.token_hex(12)+'@example.test';fixture(winner_email,'SeedPending')
    winner_browser=Browser();winner_browser.csrf();winner_body,_=start(winner_browser,winner_email)
    winner_proof={'flowId':winner_body['flowId'],'code':inbox(winner_email)}
    assert winner_browser.request('/bff/register/verify',winner_proof)[1]['status']=='SetupRequired'
    assert winner_browser.request('/bff/register/setup',winner_proof)[0]==202
    winner_code=inbox(winner_email,True);fixture(winner_email,'ProvisionOriginal')
    candidate={'flowId':winner_body['flowId'],'code':winner_code,'newPassword':'losing setup candidate password'}
    assert winner_browser.request('/bff/register/complete',candidate)==(200,{'status':'Ready','credentialOutcome':'ExistingWinner'})
    assert '__Host-smartcore-session' not in winner_browser.cookies
    assert winner_browser.request('/bff/login',{'email':winner_email,'password':candidate['newPassword']})[0]==401
    assert winner_browser.request('/bff/login',{'email':winner_email,'password':'private abandoned registration password'})[0]==200
    winner_browser.csrf();assert winner_browser.request('/bff/logout',{})[0]==204
    # Expired proof receives a generic failure, with no session creation.
    expired_email=secrets.token_hex(12)+'@example.test';expired=Browser();expired.csrf();expired_body,_=start(expired,expired_email)
    expired_code=inbox(expired_email);fixture(expired_email,'Expire')
    assert expired.request('/bff/register/verify',{'flowId':expired_body['flowId'],'code':expired_code})[1]['code']=='VERIFICATION_FAILED'
    print('B2 HTTP PASS: real TLS/two-host encrypted context, strict DTOs/guard no-dispatch budgets, lost-start replay, resend, wrong/expired OTP, proof-gated existence outcomes, cross-tab flow binding, 3600-second contact cooldown, setup/candidate completion and replay, no automatic authentication.')
    if os.environ.get('SMARTCORE_BROWSER_E2E')=='yes':
        contact=secrets.token_hex(12)+'@example.test'
        child=subprocess.Popen(['node',str(root/'web/identity/e2e/registration.mjs')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
        try:
            child.stdin.write(json.dumps({'email':contact,'password':'private Chromium registration password','tlsCertificatePath':env['SMARTCORE_ACCOUNT_TLS_CERT']})+'\n');child.stdin.flush()
            # Private pipe requests, never a browser-exposed dev inbox or operator key.
            for line in child.stdout:
                event=json.loads(line)
                if event.get('event')=='started':reply={'code':inbox(contact)}
                elif event.get('event')=='provision':fixture(contact,'Provision');reply={}
                elif event.get('event')=='passed':print('B2 CHROMIUM PASS: fa/en registration, localized OTP, pending/Ready, explicit independent login, private storage and CSP.');break
                else:raise AssertionError('B2 Chromium failed at '+event.get('stage','unknown'))
                child.stdin.write(json.dumps(reply)+'\n');child.stdin.flush()
            assert child.wait(timeout=10)==0,'B2 Chromium failed (proof output suppressed)'
        finally:
            if child.poll() is None:child.terminate();child.wait(timeout=10)
    # Unknown upstream outcome preserves registration context; never reports verified/Ready.
    prior=expired.cookies.copy();identity_process.terminate();identity_process.wait(timeout=10)
    assert expired.request('/bff/register/resend',{'flowId':expired_body['flowId']})[0]==503
    assert expired.cookies==prior
    print('B2 OUTAGE PASS: private upstream outage preserves cookies and cannot claim registration success.')
