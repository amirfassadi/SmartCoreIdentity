"""Persistent, loopback-only Stage B workspace. No provider adapter or external send.

Secrets and encrypted key ring live in ignored dev-secrets/private-stack. All child
output is suppressed (never print exception/provider/request text). This is a
private developer runner, not a production deployment or a replacement for preflight.
"""
import argparse
import base64
from contextlib import contextmanager
import fcntl
import hashlib
import json
import os
from pathlib import Path
import secrets
import signal
import socket
import stat
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / 'dev-secrets/private-stack'
PORTS = (5157, 5171, 5172, 3181, 7443)
ORIGIN = 'https://localhost:7443'
DOTNET = os.environ.get('SMARTCORE_DOTNET', 'dotnet')
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def private(path, directory=False):
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o077:
        raise ValueError('Private workspace permissions rejected')
    if not (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)):
        raise ValueError('Private workspace type rejected')


def base_env():
    # Do not inherit feature flags, remote database overrides or ASP.NET hosting settings.
    return {key: value for key, value in os.environ.items()
            if not key.lower().startswith(('identity', 'bff', 'connectionstrings', 'aspnetcore', 'dotnet_environment',
                                          'kestrel', 'next_public_', 'smartcore_'))}


def checked(command, env=None, cwd=ROOT, input=None):
    result = subprocess.run(command, cwd=cwd, env=env or base_env(), input=input,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=600)
    if result.returncode:
        raise RuntimeError('Private workspace command failed')
    return result.stdout


def write_json(path, value):
    # Exclusive creation preserves existing keys and rejects symlink targets.
    with open(path, 'x', opener=lambda name, flags: os.open(name, flags, 0o600)) as stream:
        json.dump(value, stream)


def initialize():
    (ROOT / 'dev-secrets').mkdir(mode=0o700, exist_ok=True)
    private(ROOT / 'dev-secrets', True)
    STATE.mkdir(mode=0o700, exist_ok=True)
    private(STATE, True)
    if (STATE / 'configuration.json').exists():
        load()
        print('Existing private keys and database identity preserved.')
        return
    # A partial initialization must not replace keys belonging to stored data.
    if any(STATE.iterdir()):
        raise ValueError('Incomplete initialization; inspect the private directory locally')
    (STATE / 'ring').mkdir(mode=0o700)
    (STATE / 'operator').mkdir(mode=0o700)
    config = {key: base64.b64encode(secrets.token_bytes(32)).decode() for key in
              ('mac', 'material', 'signing', 'session_mac', 'session_material')}
    config.update({key: secrets.token_hex(32) for key in ('client', 'receipt', 'operator', 'inbox', 'pfx', 'db')})
    config['project'] = 'smartcore-private-' + secrets.token_hex(6)
    checked(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '365',
             '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost,IP:127.0.0.1',
             '-keyout', str(STATE / 'tls.key'), '-out', str(STATE / 'tls.crt')])
    checked(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '365',
             '-subj', '/CN=SmartCore private wrapping', '-keyout', str(STATE / 'wrap.key'), '-out', str(STATE / 'wrap.crt')])
    checked(['openssl', 'pkcs12', '-export', '-out', str(STATE / 'wrap.pfx'), '-inkey', str(STATE / 'wrap.key'),
             '-in', str(STATE / 'wrap.crt'), '-passout', 'stdin'], input=(config['pfx'] + '\n').encode())
    for path in STATE.iterdir():
        if path.is_file():
            path.chmod(0o600)
    write_json(STATE / 'configuration.json', config)
    print('Private workspace initialized. No provider configured and no messages sent.')


def load():
    private(ROOT / 'dev-secrets', True)
    private(STATE, True)
    for name in ('configuration.json', 'tls.key', 'tls.crt', 'wrap.pfx'):
        private(STATE / name)
    for name in ('ring', 'operator'):
        private(STATE / name, True)
    config = json.loads((STATE / 'configuration.json').read_text())
    for key in ('mac', 'material', 'signing', 'session_mac', 'session_material'):
        if len(base64.b64decode(config[key], validate=True)) != 32:
            raise ValueError('Invalid private key')
    if not all(len(config[key]) == 64 and all(c in '0123456789abcdef' for c in config[key])
               for key in ('client', 'receipt', 'operator', 'inbox', 'pfx', 'db')):
        raise ValueError('Invalid private configuration')
    if not config['project'].startswith('smartcore-private-') or not config['project'][18:].isalnum():
        raise ValueError('Invalid private database identity')
    return config


@contextmanager
def lock():
    private(STATE, True)
    fd = os.open(STATE / 'runner.lock', os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, 'w') as stream:
        fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        yield


def environments(config, connection=None):
    connection = connection or f"Host=127.0.0.1;Port=55437;Database=identity_private;Username=identity_private;Password={config['db']}"
    identity = {**base_env(), 'ASPNETCORE_ENVIRONMENT': 'Development', 'ASPNETCORE_URLS': 'http://127.0.0.1:5157',
                'ConnectionStrings__Identity': connection,
                'Identity__MacKey': config['mac'], 'Identity__MaterialKey': config['material'],
                'Identity__AccessSigningKey': config['signing'], 'Identity__BffClientKey': config['client'],
                'Identity__RefreshReceiptKey': config['receipt'], 'Identity__DevInboxKey': config['inbox'],
                'Identity__AuthenticationEnabled': 'true', 'Identity__PasswordChangeEnabled': 'true',
                'Identity__RecoveryEnrollmentEnabled': 'true', 'Identity__WorkerEnabled': 'true',
                'Identity__PasswordChangeWorkerEnabled': 'true', 'Identity__ApplicationPort': '5157',
                'Identity__AuthOperatorKey': config['operator'],
                'Identity__AuthOperatorSocket': str(STATE / 'operator/recovery.sock')}
    bff = {**base_env(), 'ASPNETCORE_ENVIRONMENT': 'Development', 'ConnectionStrings__Bff': connection,
           'Bff__ExternalOrigin': ORIGIN, 'Bff__IdentityOrigin': 'http://127.0.0.1:5157',
           'Bff__ClientKey': config['client'], 'Bff__ReceiptKey': config['receipt'],
           'Bff__SessionMacKey': config['session_mac'], 'Bff__SessionMaterialKey': config['session_material'],
           'Bff__AccountsEnabled': 'true', 'Bff__RegistrationEnabled': 'true', 'Bff__RecoveryEnabled': 'true',
           'Bff__DataProtection__KeyRing': str(STATE / 'ring'),
           'Bff__DataProtection__CertificatePath': str(STATE / 'wrap.pfx'),
           'Bff__DataProtection__CertificatePassword': config['pfx']}
    return identity, bff


def compose(config, *arguments, input=None):
    return checked(['docker', 'compose', '--project-name', config['project'], '-f',
                    str(ROOT / 'deploy/development/private-db.yaml'), *arguments],
                   env={**base_env(), 'SMARTCORE_PRIVATE_DB_PASSWORD': config['db']}, input=input)


def fingerprint():
    # Reject a stale build, including changed/untracked non-ignored source.
    names = checked(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z']).split(b'\0')
    digest = hashlib.sha256()
    for name in sorted(set(names) - {b''}):
        path = ROOT / os.fsdecode(name)
        if path.is_symlink():
            raise ValueError('Source symlink rejected')
        digest.update(name + b'\0')
        digest.update(path.read_bytes() if path.exists() else b'DELETED')
    return digest.hexdigest()


def build():
    load()
    with lock():
        marker = STATE / 'build.json'
        marker.unlink(missing_ok=True)
        checked([DOTNET, 'restore', 'src/SmartCore.Identity.Api', '--locked-mode'])
        for project in ('SmartCore.Identity.Api', 'SmartCore.Identity.Bff.Host'):
            checked([DOTNET, 'build', 'src/' + project, '-c', 'Release', '-m:1'])
        checked(['npm', 'ci', '--ignore-scripts'], cwd=ROOT / 'web/identity')
        for script in ('check', 'typecheck', 'build'):
            checked(['npm', 'run', script], cwd=ROOT / 'web/identity', env={**base_env(),
                    'NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED': 'true', 'NEXT_PUBLIC_BFF_REGISTRATION_ENABLED': 'true',
                    'NEXT_PUBLIC_BFF_RECOVERY_ENABLED': 'true'})
        write_json(marker, {'fingerprint': fingerprint()})
    print('Private account frontend and backend built successfully.')


def json_request(path, data=None, key=None):
    headers = {'Content-Type': 'application/json'}
    if key:
        headers['X-Dev-Inbox-Key'] = key
    request = urllib.request.Request('http://127.0.0.1:5157' + path,
                                     data=None if data is None else json.dumps(data).encode(), headers=headers)
    with OPENER.open(request, timeout=10) as response:
        return json.loads(response.read())


def check_ports(ports=PORTS):
    for port in ports:
        with socket.socket() as probe:
            # Like Kestrel/Node, allow the previous owned listener's TIME_WAIT
            # sockets. An actively listening process must still be rejected.
            probe.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            probe.bind(('127.0.0.1', port))


def start():
    config = load()
    private(STATE / 'build.json')
    if json.loads((STATE / 'build.json').read_text())['fingerprint'] != fingerprint():
        raise ValueError('Build does not match current source; build again')
    identity, bff = environments(config)
    api = str(ROOT / 'src/SmartCore.Identity.Api/bin/Release/net10.0/SmartCore.Identity.Api.dll')
    host = str(ROOT / 'src/SmartCore.Identity.Bff.Host/bin/Release/net10.0/SmartCore.Identity.Bff.Host.dll')
    serve(config, identity, bff, [DOTNET, api], [DOTNET, host],
          ['node', str(ROOT / 'web/identity/node_modules/next/dist/bin/next'), 'start', str(ROOT / 'web/identity'),
           '--hostname', '127.0.0.1', '--port', '3181'],
          prepare_database=lambda: compose(config, 'up', '-d', '--wait', '--wait-timeout', '90', 'db'))


def serve(config, identity, bff, api_command, host_command, next_command,
          prepare_database, node='node', bff_ports=(5171, 5172)):
    """Shared foreground lifecycle for source and prebuilt private workspaces."""
    processes = []
    with lock(), tempfile.TemporaryDirectory(prefix='sci-private-') as runtime:
        # Unix socket names are bounded by sockaddr_un; repository paths can be long.
        identity['Identity__AuthOperatorSocket'] = str(Path(runtime) / 'recovery.sock')
        # Before touching the database or starting services, refuse existing listeners.
        check_ports((5157, 3181, 7443, *bff_ports))
        print('Private phase: database startup.', flush=True)
        prepare_database()
        print('Private phase: Identity migrations.', flush=True)
        checked([*api_command, '--migrate'], env=identity)
        def spawn(command, env, port, path, status):
            child = subprocess.Popen(command, env=env, cwd=ROOT, stdout=subprocess.DEVNULL,
                                     stderr=subprocess.DEVNULL, start_new_session=True)
            processes.append(child)
            until = time.monotonic() + 60
            while time.monotonic() < until:
                if any(process.poll() is not None for process in processes):
                    raise RuntimeError('Private child exited')
                try:
                    with OPENER.open(f'http://127.0.0.1:{port}{path}', timeout=1) as response:
                        if response.status == status:
                            return
                except OSError:
                    pass
                time.sleep(.1)
            raise TimeoutError('Private readiness timed out')
        def stop(_signum, _frame):
            raise KeyboardInterrupt
        previous = {sig: signal.signal(sig, stop) for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)}
        try:
            print('Private phase: Identity startup.', flush=True)
            spawn(api_command, identity, 5157, '/health/ready', 200)
            for port in bff_ports:
                print('Private phase: BFF startup.', flush=True)
                spawn(host_command, {**bff, 'ASPNETCORE_URLS': f'http://127.0.0.1:{port}'}, port, '/health/ready', 204)
            print('Private phase: frontend startup.', flush=True)
            spawn(next_command, {**base_env(), 'HOSTNAME': '127.0.0.1', 'PORT': '3181', 'NODE_ENV': 'production'}, 3181, '/fa', 200)
            print('Private phase: verified TLS startup.', flush=True)
            peers = list(bff_ports) if len(bff_ports) > 1 else [bff_ports[0], bff_ports[0]]
            ingress = subprocess.Popen([node, str(ROOT / 'deploy/development/https-ingress.mjs')], cwd=ROOT,
                        env={**base_env(), 'SMARTCORE_INGRESS_ENVIRONMENT': 'Development', 'SMARTCORE_INGRESS_ORIGIN': ORIGIN,
                             'SMARTCORE_BFF_ORIGINS': ','.join(f'http://127.0.0.1:{port}' for port in peers),
                             'SMARTCORE_TLS_KEY': str(STATE / 'tls.key'), 'SMARTCORE_TLS_CERT': str(STATE / 'tls.crt')},
                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, start_new_session=True)
            processes.append(ingress)
            import ssl
            tls = urllib.request.build_opener(urllib.request.ProxyHandler({}),
                    urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(STATE / 'tls.crt'))))
            until = time.monotonic() + 20
            while True:
                if ingress.poll() is not None:
                    raise RuntimeError('Private ingress exited')
                try:
                    with tls.open(ORIGIN + '/bff/csrf', timeout=2) as response:
                        if response.status == 200:
                            break
                except OSError:
                    pass
                if time.monotonic() > until:
                    raise TimeoutError('Private TLS readiness timed out')
                time.sleep(.1)
            print('Private workspace ready: https://localhost:7443/fa (Ctrl+C stops owned app processes).', flush=True)
            while True:
                if any(process.poll() is not None for process in processes):
                    raise RuntimeError('Private child exited')
                time.sleep(.5)
        finally:
            for sig, handler in previous.items():
                signal.signal(sig, handler)
            for process in processes:
                if process.poll() is None:
                    os.killpg(process.pid, signal.SIGTERM)
            for process in processes:
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    os.killpg(process.pid, signal.SIGKILL)
                    process.wait(timeout=5)
            print('Owned app processes stopped. Private database and keys preserved.', flush=True)


def inbox(kind, contact, query_runner=None):
    config = load()
    # SQL contains base64 only; contact text cannot inject SQL or psql commands.
    encoded = base64.b64encode(contact.strip().lower().encode()).decode()
    expression = f"convert_from(decode('{encoded}','base64'),'UTF8')"
    query = {
        'registration': f'SELECT id FROM verification_sessions WHERE contact={expression} AND expires_at>now() ORDER BY created_at DESC LIMIT 1',
        'setup': f'SELECT s.id FROM setup_challenges s JOIN verification_sessions v ON v.id=s.verification_id WHERE v.contact={expression} AND s.expires_at>now() ORDER BY s.created_at DESC LIMIT 1',
        'reset': f'SELECT c.id FROM auth_reset_challenges c JOIN persons p ON p.id=c.person_id WHERE p.contact={expression} AND c.expires_at>now() ORDER BY c.created_at DESC LIMIT 1'
    }[kind]
    if query_runner is None:
        identifier = compose(config, 'exec', '-T', 'db', 'psql', '-U', 'identity_private', '-d', 'identity_private',
                             '-At', '-v', 'ON_ERROR_STOP=1', '-c', query).decode().strip()
    else:
        identifier = query_runner(query).decode().strip()
    if not identifier or not all(c.isalnum() or c in '_-' for c in identifier):
        raise ValueError('No unexpired private proof')
    prefix = {'registration': '/dev/inbox/', 'setup': '/dev/setup-inbox/', 'reset': '/dev/reset-inbox/'}[kind]
    return json_request(prefix + identifier, key=config['inbox'])['code']


def main():
    os.umask(0o077)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('init', 'build', 'run', 'inbox', 'db-stop'))
    parser.add_argument('--kind', choices=('registration', 'setup', 'reset'), default='registration')
    args = parser.parse_args()
    try:
        if args.command == 'init':
            initialize()
        elif args.command == 'build':
            build()
        elif args.command == 'run':
            start()
        elif args.command == 'inbox':
            print(inbox(args.kind, input('Private test email or E.164 mobile: ')))
        elif args.command == 'db-stop':
            config = load()
            with lock():
                compose(config, 'stop', 'db')
            print('Private database stopped; volume preserved.')
    except KeyboardInterrupt:
        return 0
    except Exception as error:
        print(f'Private workspace failed during {args.command} ({type(error).__name__}); no raw diagnostic output.', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
