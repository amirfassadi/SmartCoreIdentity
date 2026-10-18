"""Opt-in transport-only staging for a verified bundle; account flags remain disabled."""
import argparse
import base64
from contextlib import contextmanager
import hashlib
import hmac
import importlib.util
import ipaddress
import json
import os
from pathlib import Path
import re
import signal
import stat
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('smartcore_private_bundle', ROOT / 'run.py')
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)
stack = bundle.stack
PROOF_CONTEXT = b'SmartCore.Bff.Gateway.v1'
PORTS = (5157, 5171, 5172, 5181)


def fixed_origin(value, browser=False):
    url = urllib.parse.urlsplit(value)
    host = url.hostname or ''
    port = url.port
    if (url.scheme != 'https' or url.path not in ('', '/') or url.query or url.fragment
            or url.username is not None or url.password is not None or not host or host.endswith('.')):
        raise ValueError('Fixed HTTPS origin required')
    try:
        address = ipaddress.ip_address(host)
    except ValueError:
        if (not re.fullmatch(r'(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?', host)
                or host.endswith('.localhost')):
            raise ValueError('Fixed DNS hostname required')
    else:
        if browser or not address.is_global:
            raise ValueError('Public gateway address required')
    authority = '[' + host + ']' if ':' in host else host
    if port is not None and port != 443:
        authority += ':' + str(port)
    canonical = 'https://' + authority
    if value not in (canonical, canonical + '/'):
        raise ValueError('Canonical origin required')
    return canonical


def read_settings(path):
    path = Path(path).absolute()
    stack.private(path.parent, directory=True)
    stack.private(path)
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd, 'r') as stream:
        info = os.fstat(stream.fileno())
        if info.st_uid != os.getuid() or info.st_mode & 0o077 or not stat.S_ISREG(info.st_mode):
            raise ValueError('Private settings required')
        text = stream.read(16385)
    if len(text.encode()) > 16384:
        raise ValueError('Settings too large')
    settings = {}
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith('#'):
            continue
        match = re.fullmatch(r'''([A-Z][A-Z0-9_]*)=([^\s'"`$\\]*)''', line)
        if not match or match[1] in settings:
            raise ValueError('Invalid settings syntax')
        settings[match[1]] = match[2]
    if settings.get('SMARTCORE_BFF_BRIDGE_ENABLED') not in ('true', 'false'):
        raise ValueError('Explicit bridge activation flag required')
    settings['SMARTCORE_BROWSER_ORIGIN'] = fixed_origin(settings.get('SMARTCORE_BROWSER_ORIGIN', ''), browser=True)
    settings['SMARTCORE_BFF_GATEWAY_ORIGIN'] = fixed_origin(settings.get('SMARTCORE_BFF_GATEWAY_ORIGIN', ''))
    if settings['SMARTCORE_BROWSER_ORIGIN'] == settings['SMARTCORE_BFF_GATEWAY_ORIGIN']:
        raise ValueError('Separate gateway required')
    if not re.fullmatch(r'[A-Za-z0-9_-]{43,128}', settings.get('SMARTCORE_BFF_GATEWAY_KEY', '')):
        raise ValueError('Dedicated gateway credential required')
    # This slice validates transport, not real-user account activation.
    if any(settings.get(key, 'false') != 'false' for key in (
            'NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED', 'NEXT_PUBLIC_BFF_REGISTRATION_ENABLED',
            'NEXT_PUBLIC_BFF_RECOVERY_ENABLED', 'NEXT_PUBLIC_PRIVATE_TEST_MODE')):
        raise ValueError('Public account flags must remain disabled')
    return settings


def environments(config, settings):
    connection = f"Host=127.0.0.1;Port=5432;Database={bundle.DATABASE};Username={bundle.DATABASE};Password={config['db']}"
    identity, bff = stack.environments(config, connection)
    proof = base64.urlsafe_b64encode(hmac.new(settings['SMARTCORE_BFF_GATEWAY_KEY'].encode(), PROOF_CONTEXT, hashlib.sha256).digest()).decode().rstrip('=')
    bff.update(ASPNETCORE_ENVIRONMENT='RestrictedStaging',
               Bff__RestrictedStagingEnabled='true', Bff__StagingBrowserOrigin=settings['SMARTCORE_BROWSER_ORIGIN'],
               Bff__ExternalOrigin=settings['SMARTCORE_BROWSER_ORIGIN'], Bff__GatewayProof=proof,
               Bff__AccountsEnabled='false', Bff__RegistrationEnabled='false', Bff__RecoveryEnabled='false')
    for env in (identity, bff):
        env.update(DOTNET_ROOT=str(ROOT / 'dotnet'), DOTNET_ROOT_X64=str(ROOT / 'dotnet'),
                   DOTNET_MULTILEVEL_LOOKUP='0', DOTNET_PROCESSOR_COUNT='1')
    return identity, bff


def verify_capability():
    bundle.verify()
    manifest = json.loads((ROOT / 'manifest.json').read_text())
    required = {'staging.py', 'deploy/gateway/server.mjs', 'web/identity/src/lib/bff/bridge.mjs'}
    if 'restricted-staging-v1' not in manifest.get('capabilities', []) or not required <= manifest.get('sha256', {}).keys():
        raise ValueError('A freshly packaged staging-capable bundle is required')


@contextmanager
def children():
    class Owned(list):
        stopping = False
        def check(self):
            if self.stopping:
                raise KeyboardInterrupt
    owned = Owned()
    def stop(_signum, _frame):
        # Do not raise between Popen and appending the returned child to owned.
        owned.stopping = True
    previous = {sig: signal.signal(sig, stop) for sig in (signal.SIGTERM, signal.SIGINT, signal.SIGHUP)}
    try:
        yield owned
        owned.check()
    finally:
        # Ignore repeated stop signals until all owned process groups are reaped.
        for sig in previous:
            signal.signal(sig, signal.SIG_IGN)
        try:
            for child in reversed(owned):
                if child.poll() is None:
                    try:
                        os.killpg(child.pid, signal.SIGTERM)
                    except ProcessLookupError:
                        pass
            for child in reversed(owned):
                try:
                    child.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(child.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    child.wait(timeout=5)
        finally:
            for sig, handler in previous.items():
                signal.signal(sig, handler)


def run(config, settings, settings_path):
    if settings['SMARTCORE_BFF_BRIDGE_ENABLED'] != 'true':
        raise ValueError('Bridge activation is disabled')
    identity, bff = environments(config, settings)
    dotnet = str(ROOT / 'dotnet/dotnet')
    api = [dotnet, str(ROOT / 'api/SmartCore.Identity.Api.dll')]
    host = [dotnet, str(ROOT / 'bff/SmartCore.Identity.Bff.Host.dll')]
    # The same private runner lock prevents simultaneous foreground/service owners.
    with stack.lock(), tempfile.TemporaryDirectory(prefix='sci-stage-') as temporary, children() as owned:
        stack.check_ports(PORTS)
        bundle.check_database(config)  # Marker/version verification, never provisioning or init.
        stack.checked([*host, '--validate-config'], env={**bff, 'ASPNETCORE_URLS': 'http://127.0.0.1:5171'})
        identity['Identity__AuthOperatorSocket'] = str(Path(temporary) / 'recovery.sock')
        stack.checked([*api, '--migrate'], env=identity)

        def spawn(arguments, env, port, path, expected):
            child = subprocess.Popen(arguments, env=env, cwd=ROOT, stdout=subprocess.DEVNULL,
                                     stderr=subprocess.DEVNULL, start_new_session=True)
            owned.append(child)
            until = time.monotonic() + 60
            while time.monotonic() < until:
                owned.check()
                if any(item.poll() is not None for item in owned):
                    raise RuntimeError('Owned staging child exited')
                try:
                    with stack.OPENER.open(f'http://127.0.0.1:{port}{path}', timeout=1) as response:
                        if response.status == expected:
                            return
                except OSError:
                    pass
                time.sleep(.1)
            raise TimeoutError('Staging readiness timed out')

        print('Restricted staging: starting private Identity and two BFF hosts.', flush=True)
        spawn(api, identity, 5157, '/health/ready', 200)
        for port in (5171, 5172):
            spawn(host, {**bff, 'ASPNETCORE_URLS': f'http://127.0.0.1:{port}'}, port, '/health/ready', 204)
        gateway = subprocess.Popen([str(ROOT / 'node/bin/node'), str(ROOT / 'deploy/gateway/server.mjs'), str(settings_path)],
                env={**stack.base_env(), 'SMARTCORE_BFF_ORIGINS': 'http://127.0.0.1:5171,http://127.0.0.1:5172'},
                cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
        owned.append(gateway)
        until = time.monotonic() + 20
        while time.monotonic() < until:
            owned.check()
            if any(child.poll() is not None for child in owned):
                raise RuntimeError('Owned staging child exited')
            request = urllib.request.Request('http://127.0.0.1:5181/bff/csrf', headers={
                'Host': urllib.parse.urlsplit(settings['SMARTCORE_BFF_GATEWAY_ORIGIN']).netloc,
                'X-SmartCore-Gateway-Key': settings['SMARTCORE_BFF_GATEWAY_KEY']})
            try:
                with stack.OPENER.open(request, timeout=2) as response:
                    payload = json.loads(response.read(65536))
                    if response.status == 200 and isinstance(payload.get('requestToken'), str):
                        break
            except (OSError, ValueError):
                pass
            time.sleep(.1)
        else:
            raise TimeoutError('Gateway bootstrap failed')
        print('Restricted staging transport ready on loopback 5181; account flags remain disabled.', flush=True)
        while True:
            owned.check()
            if any(child.poll() is not None for child in owned):
                raise RuntimeError('Owned staging child exited')
            time.sleep(.5)


def main():
    os.umask(0o077)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('check', 'run'))
    parser.add_argument('--settings', type=Path, default=Path('/home/amir/.config/smartcore-identity/.env'))
    args = parser.parse_args()
    try:
        if os.getuid() == 0:
            raise ValueError('Run as the existing non-root application user')
        verify_capability()
        settings = read_settings(args.settings)
        config = stack.load()
        if args.command == 'check':
            _, bff = environments(config, settings)
            bff['ASPNETCORE_URLS'] = 'http://127.0.0.1:5171'
            stack.checked([str(ROOT / 'dotnet/dotnet'), str(ROOT / 'bff/SmartCore.Identity.Bff.Host.dll'), '--validate-config'], env=bff)
            bundle.check_database(config)
            print('Staging settings, host configuration and existing database marker verified; no services started.')
        else:
            run(config, settings, args.settings.absolute())
    except KeyboardInterrupt:
        print('Owned staging processes stopped; private database and keys preserved.')
    except Exception as failure:
        print(f'Restricted staging rejected ({type(failure).__name__}); details suppressed.', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
