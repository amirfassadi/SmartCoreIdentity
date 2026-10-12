"""B1 host infrastructure only: two real processes, encrypted DP ring and JSON routing.
Includes live TLS/session-bound CSRF guards; no actual browser login/account E2E claim.
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
contract = yaml.safe_load((root / 'contracts/bff-browser.openapi.yaml').read_text())
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
        'Bff__DataProtection__KeyRing':str(directory/'ring'), 'Bff__DataProtection__CertificatePath':str(directory/'wrap.pfx'), 'Bff__DataProtection__CertificatePassword':password}
    assert run(env, '--validate-config').returncode == 0
    required = ['ASPNETCORE_URLS', 'ConnectionStrings__Bff', 'Bff__ExternalOrigin', 'Bff__IdentityOrigin',
        'Bff__ClientKey', 'Bff__ReceiptKey', 'Bff__SessionMacKey', 'Bff__SessionMaterialKey',
        'Bff__DataProtection__KeyRing', 'Bff__DataProtection__CertificatePath', 'Bff__DataProtection__CertificatePassword']
    for key in required:
        missing = env.copy(); missing.pop(key)
        assert run(missing, '--validate-config').returncode != 0, 'Missing required configuration accepted'
    print('BFF required-config PASS: healthy Testing control succeeds; each of 11 missing settings fails independently before database/network work.')
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
    identity_calls=[]
    class IdentityTrap(BaseHTTPRequestHandler):
        def log_message(self,*args): pass
        def do_POST(self):
            identity_calls.append(self.path)
            self.send_response(500);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(b'{"code":"INTERNAL_ERROR"}')
        do_GET=do_POST
    trap=ThreadingHTTPServer(('127.0.0.1',5157),IdentityTrap)
    thread=threading.Thread(target=trap.serve_forever,daemon=True);thread.start()
    processes = []
    try:
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
            for path,method,status in [('/bff/register','POST',404),('/bff/password/reset/start','POST',404),('/bff/password/reset/complete','POST',404),('/bff/csrf','POST',405),('/bff/login','GET',405),('/bff/me','GET',405),('/bff/me/action','GET',405),('/bff/logout','GET',405),('/bff/csrf','GET',503),('/bff/login','POST',415)]:
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
                if error.code == 404: break
            except OSError: pass
            time.sleep(.1)
        else: raise AssertionError('HTTPS ingress readiness timed out')
        guard=runpy.run_path(str(root/'scripts/bff-guard-checks.py'))['guard_checks']
        guard(root,tls,env,dotnet,validate_response,contract,'https://localhost:5271')
        assert not identity_calls, 'Guard/bootstrap must never dispatch to Identity'
        print('BFF GUARD DISPATCH PASS: zero Identity calls across guard, bootstrap and fixture checks.')
        for path,method,status in [('/bff/register','POST',404),('/bff/password/reset/start','POST',404),('/bff/password/reset/complete','POST',404),('/bff/csrf','POST',405),('/bff/login','GET',405),('/bff/me','GET',405),('/bff/me/action','GET',405),('/bff/logout','GET',405),('/bff/csrf','GET',200)]:
            request=urllib.request.Request('https://localhost:7443'+path,method=method,headers={'Origin':'https://localhost:7443','Sec-Fetch-Site':'same-origin','X-Bff-Client-Key':'forged','X-Forwarded-Host':'evil.invalid','X-Forwarded-Proto':'http'})
            try: response=tls.open(request,timeout=5)
            except urllib.error.HTTPError as error: response=error
            assert response.status==status, (path, method, response.status, status)
            assert response.headers['Strict-Transport-Security']=='max-age=31536000'
            validate_response(contract,path,method,status,json.loads(response.read()),dict(response.headers.items()))
        print('BFF HTTPS ingress PASS: trusted local TLS certificate, two real ASP.NET hosts, B2/B3 JSON 404, route-specific 405 Allow and no-store through the actual proxy.')
        print('BFF two-host routing PASS: B2/B3 JSON 404, route-specific 405 Allow, no-store and scaffold-unavailable responses match contract.')
    finally:
        trap.shutdown();trap.server_close();thread.join(timeout=5)
        for process in processes:
            process.terminate()
        for process in processes:
            try: process.wait(timeout=10)
            except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
