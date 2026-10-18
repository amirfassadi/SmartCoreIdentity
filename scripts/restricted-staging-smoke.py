"""Disposable CI bundle acceptance; no operator VPS or real provider calls."""
import importlib.util
import json
import os
from pathlib import Path
import secrets
import signal
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request


def main():
    root = Path(os.environ['SMARTCORE_PRIVATE_BUNDLE']).resolve()
    spec = importlib.util.spec_from_file_location('staging_acceptance', root / 'staging.py')
    stage = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(stage)
    private = root / 'dev-secrets/private-stack'
    preserved = {path: path.read_bytes() for path in [private / 'configuration.json', private / 'wrap.pfx',
                 private / 'tls.key', *sorted((private / 'ring').glob('key-*.xml'))]}
    assert preserved
    child = None
    with tempfile.TemporaryDirectory(prefix='sci-staging-settings-') as temporary:
        settings = Path(temporary) / '.env'
        key = secrets.token_urlsafe(32)
        content = '\n'.join([
            'SMARTCORE_BFF_BRIDGE_ENABLED=false',
            'SMARTCORE_BROWSER_ORIGIN=https://accounts.example.test',
            'SMARTCORE_BFF_GATEWAY_ORIGIN=https://87.107.54.244:8443',
            'SMARTCORE_BFF_GATEWAY_KEY=' + key]) + '\n'
        settings.write_text(content); settings.chmod(0o600)
        command = [sys.executable, str(root / 'staging.py')]
        subprocess.run([*command, 'check', '--settings', str(settings)], check=True, timeout=60)
        denied = subprocess.run([*command, 'run', '--settings', str(settings)], capture_output=True, timeout=30)
        assert denied.returncode != 0, 'Disabled bridge started'
        stage.stack.check_ports(stage.PORTS)
        settings.write_text(content.replace('BRIDGE_ENABLED=false', 'BRIDGE_ENABLED=true'))

        def request(path='/bff/csrf', body=None, headers=None):
            wire = urllib.request.Request('http://127.0.0.1:5181' + path,
                data=None if body is None else json.dumps(body).encode(), headers={
                    'Host': '87.107.54.244:8443', 'X-SmartCore-Gateway-Key': key,
                    'Origin': 'https://accounts.example.test', 'Content-Type': 'application/json', **(headers or {})})
            try:
                response = stage.stack.OPENER.open(wire, timeout=3)
            except urllib.error.HTTPError as error:
                response = error
            with response:
                return response.status, json.loads(response.read()), response.headers

        def launch():
            process = subprocess.Popen([*command, 'run', '--settings', str(settings)])
            try:
                until = time.monotonic() + 120
                while time.monotonic() < until:
                    assert process.poll() is None, 'Staging runner exited'
                    try:
                        if request()[0] == 200:
                            return process
                    except OSError:
                        pass
                    time.sleep(.2)
                raise AssertionError('Staging startup deadline')
            except BaseException:
                if process.poll() is None:
                    process.terminate(); process.wait(timeout=50)
                raise

        def stop(process):
            process.send_signal(signal.SIGHUP)
            assert process.wait(timeout=50) == 0, 'Staging runner did not stop cleanly'
            stage.stack.check_ports(stage.PORTS)

        try:
            child = launch()
            assert request(headers={'X-SmartCore-Gateway-Key': 'incorrect'})[0] == 403
            assert request('/auth/login', {})[0] == 404
            status, payload, headers = request()
            assert status == 200
            cookie = '; '.join(value.split(';')[0] for value in headers.get_all('Set-Cookie', []))
            assert '__Host-smartcore-antiforgery=' in cookie
            status, payload, _ = request('/bff/login', {'email': 'transport@example.test', 'password': 'test-only-password'},
                {'Cookie': cookie, 'X-CSRF-Token': payload['requestToken']})
            assert status == 503 and payload['code'] == 'BFF_UNAVAILABLE', 'Accounts not disabled after CSRF'
            stop(child); child = None
            child = launch()
            assert request()[0] == 200
            for path, before in preserved.items():
                assert path.read_bytes() == before, 'Private keys or existing key-ring contents changed'
            print('Prebuilt staging PASS: disabled activation refused; real two-BFF gateway bootstrap, key/path rejection, accounts disabled, clean restart and prior private keys preserved.')
        finally:
            if child is not None and child.poll() is None:
                stop(child)


if __name__ == '__main__':
    try:
        main()
    except Exception as failure:
        print('Prebuilt staging acceptance failed (' + type(failure).__name__ + '); details suppressed.', file=sys.stderr)
        sys.exit(1)
