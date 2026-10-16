"""Real private launcher: registration, two peers, restart and persistent auth.

Requires tools/private_stack.py init/build. Uses the runner's own private PostgreSQL
volume, never an external/existing database. No production or provider calls.
"""
import http.cookiejar
import importlib.util
import json
import os
from pathlib import Path
import secrets
import signal
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
BUNDLE = Path(os.environ['SMARTCORE_PRIVATE_BUNDLE']).resolve() if os.environ.get('SMARTCORE_PRIVATE_BUNDLE') else None
spec = importlib.util.spec_from_file_location('private_stack', (BUNDLE or ROOT) / 'tools/private_stack.py')
stack = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stack)
if BUNDLE:
    wrapper_spec = importlib.util.spec_from_file_location('private_bundle', BUNDLE / 'run.py')
    wrapper = importlib.util.module_from_spec(wrapper_spec)
    wrapper_spec.loader.exec_module(wrapper)


def run():
    context = ssl.create_default_context(cafile=str(stack.STATE / 'tls.crt'))
    cookies = http.cookiejar.CookieJar()
    browser = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context),
                                         urllib.request.HTTPCookieProcessor(cookies))
    token = None
    child = None

    def request(path, body=None):
        headers = {'Content-Type': 'application/json', 'Origin': stack.ORIGIN, 'Sec-Fetch-Site': 'same-origin'}
        if token:
            headers['X-CSRF-Token'] = token
        wire = urllib.request.Request(stack.ORIGIN + path, headers=headers,
                        data=None if body is None else json.dumps(body).encode())
        try:
            response = browser.open(wire, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        raw = response.read()
        return response.status, json.loads(raw) if raw else None

    def launch():
        process = subprocess.Popen([sys.executable, str(BUNDLE / 'run.py' if BUNDLE else ROOT / 'tools/private_stack.py'), 'run'],
                    stdout=None, stderr=None)
        until = time.monotonic() + 180
        while time.monotonic() < until:
            if process.poll() is not None:
                raise AssertionError('Private runner exited before readiness')
            try:
                if request('/bff/csrf')[0] == 200:
                    return process
            except OSError:
                pass
            time.sleep(.25)
        process.terminate()
        process.wait(timeout=60)
        raise AssertionError('Private runner readiness deadline')

    def stop(process, requested_signal=signal.SIGTERM):
        process.send_signal(requested_signal)
        if process.wait(timeout=60) != 0:
            raise AssertionError('Private runner did not stop cleanly')

    try:
        child = launch()
        print('Private acceptance phase: initial startup ready.', flush=True)
        token = request('/bff/csrf')[1]['requestToken']
        # Neither inbox nor raw Identity routes are available through the ingress.
        assert request('/dev/inbox/missing')[0] == 404
        assert request('/auth/login', {})[0] == 404
        print('Private acceptance phase: diagnostic routes blocked.', flush=True)
        contact = secrets.token_hex(12) + '@example.test'
        password = secrets.token_urlsafe(24)
        flow = secrets.token_urlsafe(32)
        assert request('/bff/register', {'flowId': flow, 'email': contact,
                                       'password': password, 'displayName': 'Private workspace test'})[0] == 202
        proof = {'flowId': flow, 'code': stack.inbox('registration', contact,
                  query_runner=(lambda sql: wrapper.query(stack.load(), sql)) if BUNDLE else None)}
        print('Private acceptance phase: registration requested; local inbox available.', flush=True)
        until = time.monotonic() + 30
        while True:
            status, result = request('/bff/register/verify', proof)
            assert status == 200
            if result['status'] == 'Ready':
                break
            assert result['status'] == 'PendingCredential'
            assert time.monotonic() < until
            time.sleep(.3)
        print('Private acceptance phase: registration Ready.', flush=True)
        assert request('/bff/login', {'email': contact, 'password': password})[0] == 200
        token = request('/bff/csrf')[1]['requestToken']
        # Source workspace alternates two peers; the low-memory bundle has one.
        for _ in range(4):
            assert request('/bff/me', {})[0] == 200
        before = {item.name: item.value for item in cookies if item.name == '__Host-smartcore-session'}
        assert before
        peers = 'single-host reads' if BUNDLE else 'two-host reads'
        print('Private acceptance phase: login and ' + peers + ' accepted.', flush=True)
        # An SSH hangup must clean owned process groups, release the lock and
        # allow the same persistent workspace/session to resume immediately.
        stop(child, signal.SIGHUP)
        child = None
        child = launch()
        print('Private acceptance phase: restarted.', flush=True)
        token = request('/bff/csrf')[1]['requestToken']
        assert request('/bff/me', {})[0] == 200
        assert before == {item.name: item.value for item in cookies if item.name == '__Host-smartcore-session'}
        print('Private acceptance phase: original session preserved.', flush=True)
        assert request('/bff/logout', {})[0] == 204
        token = request('/bff/csrf')[1]['requestToken']
        assert request('/bff/me', {})[0] == 401
        print('Private runner PASS: real registration/login/' + peers + '/logout and session continuity after restart; no provider calls.')
    finally:
        if child is not None and child.poll() is None:
            stop(child)


if __name__ == '__main__':
    try:
        run()
    except Exception as error:
        print('Private runner acceptance failed (' + type(error).__name__ + '); proof-bearing diagnostics suppressed.', file=sys.stderr)
        sys.exit(1)
