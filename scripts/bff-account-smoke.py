"""B1 host infrastructure only: two real processes, encrypted DP ring and JSON routing.
Real private Identity account HTTP acceptance; browser coverage is a separate assertion.
"""
import base64
import json
import os
from pathlib import Path
import secrets
import ssl
import runpy
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import yaml

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / 'tools'))
from check_bff_browser_contract import validate_response
from bff_browser_contract import load_contract
b3 = os.environ.get('SMARTCORE_B3') == 'yes'
b2 = os.environ.get('SMARTCORE_B2') == 'yes' or b3
contract = load_contract(registration=b2,recovery=b3)
dotnet = os.environ.get('SMARTCORE_DOTNET', 'dotnet')
assembly = str(root / 'src/SmartCore.Identity.Bff.Host/bin/Release/net10.0/SmartCore.Identity.Bff.Host.dll')
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

def run(env, flag, text=''):
    return subprocess.run([dotnet, assembly, flag], env=env, input=text, text=True, capture_output=True, timeout=30)

with tempfile.TemporaryDirectory(prefix='smartcore-bff-host-') as scratch:
    directory = Path(scratch); (directory / 'ring').mkdir(mode=0o700)
    password = secrets.token_urlsafe(32)
    subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-keyout',str(directory/'key.pem'),'-out',str(directory/'cert.pem'),'-days','1','-nodes','-subj','/CN=SmartCore local B1 proof'], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    subprocess.run(['openssl','pkcs12','-export','-out',str(directory/'wrap.pfx'),'-inkey',str(directory/'key.pem'),'-in',str(directory/'cert.pem'),'-passout','stdin'], input=(password+'\n').encode(), check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    env = {**os.environ, 'ASPNETCORE_ENVIRONMENT':'Testing', 'ASPNETCORE_URLS':'http://127.0.0.1:5171',
        'ConnectionStrings__Bff':os.environ.get('ConnectionStrings__Bff', 'Host=127.0.0.1;Database=unused_config_only'),
        'Bff__ExternalOrigin':'https://localhost:7443', 'Bff__IdentityOrigin':'http://127.0.0.1:5157/', 'Bff__ClientKey':secrets.token_urlsafe(32), 'Bff__ReceiptKey':secrets.token_urlsafe(32),
        'Bff__SessionMacKey':base64.b64encode(secrets.token_bytes(32)).decode(), 'Bff__SessionMaterialKey':base64.b64encode(secrets.token_bytes(32)).decode(),
        'Bff__AccountsEnabled':'true', 'Bff__RegistrationEnabled':'true' if b2 else 'false', 'Bff__RecoveryEnabled':'true' if b3 else 'false', 'Bff__DataProtection__KeyRing':str(directory/'ring'), 'Bff__DataProtection__CertificatePath':str(directory/'wrap.pfx'), 'Bff__DataProtection__CertificatePassword':password}
    assert run(env, '--validate-config').returncode == 0
    for overrides in [{'ASPNETCORE_ENVIRONMENT':'Production'}, {'ASPNETCORE_URLS':'http://0.0.0.0:5171'}, {'Bff__DataProtection__CertificatePath':str(directory/'missing.pfx')}, {'Bff__ReceiptKey':env['Bff__ClientKey']}, {'Bff__ExternalOrigin':'http://localhost:7443'}]:
        assert run({**env, **overrides}, '--validate-config').returncode != 0
    first = run(env, '--keyring-proof'); assert first.returncode == 0
    protected = first.stdout.strip().splitlines()[-1]
    second = run({**env, 'ASPNETCORE_URLS':'http://127.0.0.1:5172'}, '--keyring-proof', protected+'\n')
    assert second.returncode == 0 and 'Shared encrypted key ring proof accepted.' in second.stdout
    keys = list((directory/'ring').glob('key-*.xml')); assert keys
    assert all('encryptedSecret' in key.read_text() and '<masterKey' not in key.read_text() for key in keys)
    print('BFF host config PASS: Production/public bind/missing certificate/equal keys rejected; two-process encrypted shared-ring proof accepted.')
    if '--config-only' in sys.argv:
        sys.exit(0)
    assert os.environ.get('ConnectionStrings__Bff'), 'Provide a disposable PostgreSQL 17 test database'
    # TLS endpoints use their own key, separate from Data Protection wrapping material.
    subprocess.run(['openssl','req','-x509','-newkey','rsa:2048','-nodes','-days','1','-subj','/CN=localhost','-addext','subjectAltName=DNS:localhost,IP:127.0.0.1','-keyout',str(directory/'tls.key'),'-out',str(directory/'tls.crt')],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    subprocess.run(['openssl','pkcs12','-export','-out',str(directory/'tls.pfx'),'-inkey',str(directory/'tls.key'),'-in',str(directory/'tls.crt'),'-passout','stdin'],input=(password+'\n').encode(),check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    env['SMARTCORE_ACCOUNT_TLS_CERT']=str(directory/'tls.crt')
    identity_env = {**env, 'ASPNETCORE_ENVIRONMENT':'Development', 'ASPNETCORE_URLS':'http://127.0.0.1:5157',
        'ConnectionStrings__Identity':env['ConnectionStrings__Bff'],
        'Identity__MacKey':base64.b64encode(secrets.token_bytes(32)).decode(),
        'Identity__MaterialKey':base64.b64encode(secrets.token_bytes(32)).decode(),
        'Identity__AccessSigningKey':base64.b64encode(secrets.token_bytes(32)).decode(),
        'Identity__RefreshReceiptKey':env['Bff__ReceiptKey'], 'Identity__BffClientKey':env['Bff__ClientKey'],
        'Identity__AuthenticationEnabled':'true','Identity__WorkerEnabled':'false' if b2 else 'true','Identity__DevInboxKey':secrets.token_urlsafe(32)}
    if b3:
        (directory/'operator').mkdir(mode=0o700)
        identity_env.update(Identity__PasswordChangeEnabled='true',Identity__RecoveryEnrollmentEnabled='true',Identity__PasswordChangeWorkerEnabled='false',Identity__AuthOperatorKey=secrets.token_urlsafe(32),Identity__AuthOperatorSocket=str(directory/'operator'/'recovery.sock'),Identity__ApplicationPort='5157')
    identity_assembly=str(root/'src/SmartCore.Identity.Api/bin/Release/net10.0/SmartCore.Identity.Api.dll')
    subprocess.run([dotnet,identity_assembly,'--migrate'],env=identity_env,check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    processes = []
    try:
        identity_process=subprocess.Popen([dotnet,identity_assembly],env=identity_env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        processes.append(identity_process)
        for _ in range(200):
            assert identity_process.poll() is None,'Identity exited before readiness'
            try:
                if opener.open('http://127.0.0.1:5157/health/ready',timeout=1).status==200:break
            except OSError:pass
            time.sleep(.1)
        else:raise AssertionError('Identity readiness timed out')
        for port in (5171,5172):
            process = subprocess.Popen([dotnet, assembly], env={**env,'ASPNETCORE_URLS':f'http://127.0.0.1:{port};https://127.0.0.1:{port+100}', 'Kestrel__Certificates__Default__Path':str(directory/'tls.pfx'), 'Kestrel__Certificates__Default__Password':password}, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            processes.append(process)
            for _ in range(200):
                assert process.poll() is None, 'BFF host exited before readiness'
                try:
                    if opener.open(f'http://127.0.0.1:{port}/health/ready',timeout=1).status == 204: break
                except OSError: pass
                time.sleep(.1)
            else: raise AssertionError('BFF host readiness timed out')
            for path,method,status in [('/bff/register','POST',415 if b2 else 404),('/bff/password/reset/start','POST',404),('/bff/password/reset/complete','POST',415 if b3 else 404),('/bff/csrf','POST',405),('/bff/login','GET',405),('/bff/me','GET',405),('/bff/me/action','GET',405),('/bff/logout','GET',405),('/bff/csrf','GET',503),('/bff/login','POST',415)]:
                request = urllib.request.Request(f'http://127.0.0.1:{port}{path}',method=method)
                try: response = opener.open(request,timeout=5)
                except urllib.error.HTTPError as error: response = error
                assert response.status == status, (path, method, response.status, status)
                validate_response(contract,path,method,status,json.loads(response.read()),dict(response.headers.items()))
        ingress = subprocess.Popen(['node',str(root/'deploy/development/https-ingress.mjs')],env={**os.environ,
            'SMARTCORE_INGRESS_ENVIRONMENT':'Testing','SMARTCORE_INGRESS_ORIGIN':'https://localhost:7443',
            'SMARTCORE_TLS_KEY':str(directory/'tls.key'),'SMARTCORE_TLS_CERT':str(directory/'tls.crt')},stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        processes.append(ingress)
        tls = urllib.request.build_opener(urllib.request.ProxyHandler({}),urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(directory/'tls.crt'))))
        for _ in range(100):
            assert ingress.poll() is None, 'HTTPS ingress exited before readiness'
            try:
                tls.open('https://localhost:7443/bff/register',timeout=1)
            except urllib.error.HTTPError as error:
                if error.code == (405 if b2 else 404): break
            except OSError: pass
            time.sleep(.1)
        else: raise AssertionError('HTTPS ingress readiness timed out')
        if os.environ.get('SMARTCORE_BROWSER_E2E')=='yes':
            next_process=subprocess.Popen(['node',str(root/'web/identity/node_modules/next/dist/bin/next'),'start','--hostname','127.0.0.1','--port','3181'],cwd=root/'web/identity',stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            processes.append(next_process)
            for _ in range(100):
                assert next_process.poll() is None,'Next exited before browser acceptance'
                try:
                    if opener.open('http://127.0.0.1:3181/en',timeout=1).status==200:break
                except OSError:pass
                time.sleep(.1)
            else:raise AssertionError('Next readiness timed out')
        checks=runpy.run_path(str(root/('scripts/bff-recovery-checks.py' if b3 else 'scripts/bff-registration-checks.py' if b2 else 'scripts/bff-account-checks.py')))['recovery_checks' if b3 else 'registration_checks' if b2 else 'account_checks']
        checks(root,tls,opener,env,identity_env,dotnet,validate_response,contract,identity_process)
    finally:
        for process in processes:
            process.terminate()
        for process in processes:
            try: process.wait(timeout=10)
            except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
