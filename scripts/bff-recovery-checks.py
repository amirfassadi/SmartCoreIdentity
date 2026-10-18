"""Real B3 wire contract checks. One-time codes/proofs stay in private pipes/memory."""
from http.cookies import SimpleCookie
import json
import os
import secrets
import subprocess
import time
import urllib.error
import urllib.request


def recovery_checks(root,tls,private,env,identity_env,dotnet,validate_response,contract,identity_process):
    origin='https://localhost:7443'
    def fixture(contact,action='Inspect',registration=False):
        data={'Connection':env['ConnectionStrings__Bff'],'MacKey':identity_env['Identity__MacKey'],'MaterialKey':identity_env['Identity__MaterialKey'],
            'SigningKey':identity_env['Identity__AccessSigningKey'],'Action':action,'Contact':contact}
        child=subprocess.run([dotnet,str(root/'tests/SmartCore.Identity.Tests/bin/Release/net10.0/SmartCore.Identity.Tests.dll'),
            '--browser-registration-fixture' if registration else '--browser-recovery-fixture'],input=json.dumps(data)+'\n',capture_output=True,text=True,
            timeout=30,env={**os.environ,'SMARTCORE_TEST_ALLOW_RESET':'yes'})
        assert child.returncode==0,'Private recovery fixture failed';return json.loads(child.stdout.strip().splitlines()[-1])
    def inbox(contact,registration=False):
        row=fixture(contact,registration=registration);path='/dev/inbox/'+row['Verification'] if registration else '/dev/reset-inbox/'+row['Challenge']
        request=urllib.request.Request('http://127.0.0.1:5157'+path,headers={'X-Dev-Inbox-Key':identity_env['Identity__DevInboxKey']})
        with private.open(request,timeout=10) as response:return json.loads(response.read())['code']
    class Browser:
        def __init__(self):self.cookies={};self.token=None
        def request(self,path,body=None,overrides=None):
            headers={'Content-Type':'application/json','Origin':origin,'Sec-Fetch-Site':'same-origin','Cookie':'; '.join(k+'='+v for k,v in self.cookies.items())}
            if self.token:headers['X-CSRF-Token']=self.token
            headers.update(overrides or {})
            request=urllib.request.Request(origin+path,data=None if body is None else json.dumps(body).encode(),headers=headers)
            try:response=tls.open(request,timeout=30)
            except urllib.error.HTTPError as failure:response=failure
            wire=response.read();value=json.loads(wire) if wire else None
            validate_response(contract,path,request.get_method(),response.status,value,{k:response.headers.get_all(k) for k in response.headers.keys()})
            assert not any(k.encode() in wire for k in ('personId','sessionId','operationId','challengeId','bindingSecret','accessToken','refreshToken'))
            for field in response.headers.get_all('Set-Cookie',[]):
                parsed=SimpleCookie();parsed.load(field)
                for key,cookie in parsed.items():
                    if cookie.value:self.cookies[key]=cookie.value
                    else:self.cookies.pop(key,None)
            return response.status,value
        def csrf(self):status,data=self.request('/bff/csrf');assert status==200;self.token=data['requestToken']
    def seed(contact,password):
        browser=Browser();browser.csrf();flow=secrets.token_urlsafe(32)
        assert browser.request('/bff/register',{'flowId':flow,'email':contact,'password':password,'displayName':'B3 fixture'})[0]==202
        assert browser.request('/bff/register/verify',{'flowId':flow,'code':inbox(contact,True)})[1]['status']=='PendingCredential'
        fixture(contact,'Provision',True)
        assert browser.request('/bff/login',{'email':contact,'password':password})[0]==200;browser.csrf();return browser
    contact=secrets.token_hex(12)+'@example.test';old='private B3 original password';new='private B3 reset replacement password'
    browser=seed(contact,old);session_cookies=browser.cookies.copy()
    enroll={'flowId':secrets.token_urlsafe(32),'currentPassword':old,'acceptLossRisk':True}
    assert browser.request('/bff/recovery/enroll',{**enroll,'acceptLossRisk':False})[0]==400
    assert browser.request('/bff/recovery/enroll',{**enroll,'currentPassword':'wrong password'})==(400,{'code':'CREDENTIALS_REJECTED'})
    assert browser.cookies==session_cookies and browser.request('/bff/me',{})[0]==200
    assert browser.request('/bff/recovery/enroll',{**enroll,'personId':'forbidden'})[0]==400
    status,issued=browser.request('/bff/recovery/enroll',enroll);assert status==200 and issued['status']=='Issued';recovery=issued['recoveryCode']
    assert browser.request('/bff/recovery/enroll',enroll)==(200,{'status':'AlreadyIssued'})
    # Other real session must close at reset acceptance, before Credential processing.
    peer=Browser();peer.csrf();assert peer.request('/bff/login',{'email':contact,'password':old})[0]==200;peer.csrf()
    assert browser.request('/bff/logout',{})[0]==204
    reset=Browser();reset.csrf();flow=secrets.token_urlsafe(32);start={'flowId':flow,'email':contact}
    status,pending=reset.request('/bff/password/reset',start);assert status==202
    assert reset.request('/bff/password/reset',start)==(202,pending)
    assert '__Host-smartcore-session' not in reset.cookies
    code=inbox(contact);complete={'flowId':flow,'code':code,'recoveryCode':recovery,'newPassword':new}
    before=fixture(contact)
    assert reset.request('/bff/password/reset/complete',{**complete,'flowId':secrets.token_urlsafe(32)})==(400,{'code':'RESET_FLOW_EXPIRED'})
    assert reset.request('/bff/password/reset/complete',{**complete,'verified':True})[0]==400
    assert fixture(contact)['Stage']==before['Stage'] and fixture(contact)['HasRecovery']
    wrong='00000000' if code!='00000000' else '99999999'
    assert reset.request('/bff/password/reset/complete',{**complete,'code':wrong})==(400,{'code':'RESET_PROOF_REJECTED'})
    other='0'*32 if recovery!='0'*32 else '1'*32
    assert reset.request('/bff/password/reset/complete',{**complete,'recoveryCode':other})==(400,{'code':'RESET_PROOF_REJECTED'})
    assert reset.request('/bff/password/reset/complete',complete)==(202,{'status':'Accepted'})
    assert reset.request('/bff/password/reset/complete',complete)==(202,{'status':'Accepted'}) # Explicit lost-response replay, no new fence.
    # Six source admissions including both explicit replays; never automatically retry.
    assert fixture(contact)['Stage']=='Fenced' and fixture(contact)['ActiveSessions']==0
    assert '__Host-smartcore-session' not in reset.cookies
    assert peer.request('/bff/me',{})[0]==401
    assert reset.request('/bff/login',{'email':contact,'password':new})==(401,{'code':'UNAUTHORIZED'})
    assert reset.request('/bff/login',{'email':contact,'password':old})==(401,{'code':'UNAUTHORIZED'})
    assert fixture(contact)['ActiveSessions']==0 # Uniform denial while fenced, no premature session.
    fixture(contact,'Process');assert fixture(contact)['Stage']=='Reconciled' and not fixture(contact)['HasRecovery']
    assert reset.request('/bff/login',{'email':contact,'password':old})[0]==401
    assert reset.request('/bff/login',{'email':contact,'password':new})[0]==200;reset.csrf()
    assert reset.request('/bff/me',{})[1]['person']['recoveryEnrollmentRequired']
    replacement={'flowId':secrets.token_urlsafe(32),'currentPassword':new,'acceptLossRisk':True}
    status,reissued=reset.request('/bff/recovery/enroll',replacement);assert status==200 and reissued['recoveryCode']!=recovery
    assert reset.request('/bff/recovery/enroll',replacement)==(200,{'status':'AlreadyIssued'})
    current_cookies=reset.cookies.copy();final='private B3 changed password'
    change={'flowId':secrets.token_urlsafe(32),'currentPassword':new,'newPassword':final}
    assert reset.request('/bff/password/change',{**change,'currentPassword':'wrong password'})==(400,{'code':'CREDENTIALS_REJECTED'})
    assert reset.cookies==current_cookies
    assert reset.request('/bff/password/change',change)==(202,{'status':'Accepted'})
    assert '__Host-smartcore-session' not in reset.cookies and '__Host-smartcore-antiforgery' not in reset.cookies
    assert fixture(contact)['ActiveSessions']==0;fixture(contact,'Process');reset.csrf()
    assert reset.request('/bff/login',{'email':contact,'password':final})[0]==200;reset.csrf()
    assert reset.request('/bff/me',{})[0]==200
    # Unknown/un-enrolled contacts return exactly the same initiation shape.
    anonymous=Browser();anonymous.csrf();unknown=secrets.token_hex(12)+'@example.test'
    status,decoy=anonymous.request('/bff/password/reset',{'flowId':secrets.token_urlsafe(32),'email':unknown});assert status==202 and set(decoy)==set(pending)
    assert not any(k.startswith('__Host-smartcore-session') for k in anonymous.cookies)
    print('B3 HTTP PASS: protected one-time enrollment/no-code replay, current-password failure preserves session, both reset factors required, uniform initiation, flow/DTO no-authority checks, durable fencing before Credential work, old sessions rejected, new-password login, consumed-code replacement enrollment and password change/local cookie clear.')
    if os.environ.get('SMARTCORE_BROWSER_E2E')=='yes':
        # Independent account/source buckets for the complete real browser journey.
        chromium_contact=secrets.token_hex(12)+'@example.test'
        child=subprocess.Popen(['node',str(root/'web/identity/e2e/recovery.mjs')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
        try:
            child.stdin.write(json.dumps({'email':chromium_contact,'password':'private B3 Chromium password','newPassword':'private B3 Chromium reset password','finalPassword':'private B3 Chromium changed password','tlsCertificatePath':env['SMARTCORE_ACCOUNT_TLS_CERT']})+'\n');child.stdin.flush()
            refresh_generation=None
            for line in child.stdout:
                event=json.loads(line);kind=event.get('event')
                if kind=='registered':reply={'code':inbox(chromium_contact,True)}
                elif kind=='provision':fixture(chromium_contact,'Provision',True);reply={}
                elif kind=='refresh-due':refresh_generation=fixture(chromium_contact,'Due')['Generation'];reply={}
                elif kind=='refresh-done':
                    assert fixture(chromium_contact)['Generation']==refresh_generation+1,'Actual browser action did not rotate exactly once'
                    reply={}
                elif kind=='reset-started':reply={'code':inbox(chromium_contact)}
                elif kind=='process':fixture(chromium_contact,'Process');reply={}
                elif kind=='passed':print('B3 CHROMIUM PASS: registration/login, proven single browser refresh, one-time recovery consent/acknowledgment, logout, two-factor reset, pending/new-password login, replacement enrollment, password change and cross-tab rejection; fa/en/CSP/private storage.');break
                else:raise AssertionError('B3 Chromium failed at '+event.get('stage','unknown'))
                child.stdin.write(json.dumps(reply)+'\n');child.stdin.flush()
            assert child.wait(timeout=10)==0,'B3 Chromium failed (proof output suppressed)'
        finally:
            if child.poll() is None:child.terminate();child.wait(timeout=10)
    # A local refresh-lock failure BEFORE upstream dispatch preserves cookies,
    # creates no mutation intent and never claims a completed change.
    busy_contact=secrets.token_hex(12)+'@example.test';busy_old='private contended original password';busy_new='private contended replacement password'
    busy=seed(busy_contact,busy_old);prior_busy=busy.cookies.copy()
    config={'Connection':env['ConnectionStrings__Bff'],'IdentityConnection':env['ConnectionStrings__Bff'],
        'MacKey':env['Bff__SessionMacKey'],'MaterialKey':env['Bff__SessionMaterialKey'],'Action':'Hold','Cookie':busy.cookies['__Host-smartcore-session']}
    hold=subprocess.Popen([dotnet,str(root/'tests/SmartCore.Identity.Tests/bin/Release/net10.0/SmartCore.Identity.Tests.dll'),'--browser-account-fixture'],
        stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,env={**os.environ,'SMARTCORE_TEST_ALLOW_RESET':'yes'})
    try:
        hold.stdin.write(json.dumps(config)+'\n');hold.stdin.flush();assert hold.stdout.readline().strip()=='Ready'
        status,data=busy.request('/bff/password/change',{'flowId':secrets.token_urlsafe(32),'currentPassword':busy_old,'newPassword':busy_new})
        assert status==503 and data['code']=='BFF_BUSY' and busy.cookies==prior_busy
        assert fixture(busy_contact)['Stage'] is None and fixture(busy_contact)['ActiveSessions']==1
    finally:
        hold.stdin.write('release\n');hold.stdin.flush();hold.wait(timeout=10)
    assert busy.request('/bff/password/change',{'flowId':secrets.token_urlsafe(32),'currentPassword':busy_old,'newPassword':busy_new})[0]==202
    fixture(busy_contact,'Process');busy.csrf()
    assert busy.request('/bff/login',{'email':busy_contact,'password':busy_new})[0]==200
    print('B3 PRE-DISPATCH BUSY PASS: row contention preserves cookies and creates no upstream mutation; new explicit action completes after release/reconciliation.')
    prior=anonymous.cookies.copy();identity_process.terminate();identity_process.wait(timeout=10)
    assert anonymous.request('/bff/password/reset',{'flowId':secrets.token_urlsafe(32),'email':unknown})[0]==503
    assert anonymous.cookies==prior
    print('B3 OUTAGE PASS: ambiguous upstream preserves proof cookies and never claims accepted reset.')
