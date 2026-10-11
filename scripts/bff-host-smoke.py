"""B1 host infrastructure only: two real processes, encrypted DP ring and JSON routing.
No session-bound CSRF, auth flow, HTTPS proxy or browser acceptance claim.
"""
import base64
import json
import os
from pathlib import Path
import secrets
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
        'Bff__IdentityOrigin':'http://127.0.0.1:5157/', 'Bff__ClientKey':secrets.token_urlsafe(32), 'Bff__ReceiptKey':secrets.token_urlsafe(32),
        'Bff__SessionMacKey':base64.b64encode(secrets.token_bytes(32)).decode(), 'Bff__SessionMaterialKey':base64.b64encode(secrets.token_bytes(32)).decode(),
        'Bff__DataProtection__KeyRing':str(directory/'ring'), 'Bff__DataProtection__CertificatePath':str(directory/'wrap.pfx'), 'Bff__DataProtection__CertificatePassword':password}
    assert run(env, '--validate-config').returncode == 0
    for overrides in [{'ASPNETCORE_ENVIRONMENT':'Production'}, {'ASPNETCORE_URLS':'http://0.0.0.0:5171'}, {'Bff__DataProtection__CertificatePath':str(directory/'missing.pfx')}, {'Bff__ReceiptKey':env['Bff__ClientKey']}]:
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
    processes = []
    try:
        for port in (5171,5172):
            process = subprocess.Popen([dotnet, assembly], env={**env,'ASPNETCORE_URLS':f'http://127.0.0.1:{port}'}, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            processes.append(process)
            for _ in range(200):
                assert process.poll() is None, 'BFF host exited before readiness'
                try:
                    if opener.open(f'http://127.0.0.1:{port}/health/ready',timeout=1).status == 204: break
                except OSError: pass
                time.sleep(.1)
            else: raise AssertionError('BFF host readiness timed out')
            for path,method,status in [('/bff/register','POST',404),('/bff/password/reset/start','POST',404),('/bff/password/reset/complete','POST',404),('/bff/csrf','POST',405),('/bff/login','GET',405),('/bff/me','GET',405),('/bff/me/action','GET',405),('/bff/logout','GET',405),('/bff/csrf','GET',503),('/bff/login','POST',503)]:
                request = urllib.request.Request(f'http://127.0.0.1:{port}{path}',method=method)
                try: response = opener.open(request,timeout=5)
                except urllib.error.HTTPError as error: response = error
                assert response.status == status
                validate_response(contract,path,method,status,json.loads(response.read()),dict(response.headers.items()))
        print('BFF two-host routing PASS: B2/B3 JSON 404, route-specific 405 Allow, no-store and scaffold-unavailable responses match contract.')
    finally:
        for process in processes:
            process.terminate()
        for process in processes:
            try: process.wait(timeout=10)
            except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=5)
