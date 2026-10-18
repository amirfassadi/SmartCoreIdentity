"""Inventory for the existing Identity bundle. Never reads credentials or changes services."""
import json
import os
from pathlib import Path
import re
import shutil
import stat
import subprocess
import urllib.request

BUNDLE = Path('/home/amir/smartcore-identity-private')


def command(arguments, allowed):
    try:
        result = subprocess.run(arguments, capture_output=True, text=True, timeout=8)
        matches = [line for line in result.stdout.splitlines() if re.fullmatch(allowed, line)]
        return '; '.join(matches) if matches else 'unavailable'
    except (OSError, subprocess.TimeoutExpired):
        return 'unavailable'


def metadata(path):
    try:
        info = path.lstat()
        kind = 'symlink' if stat.S_ISLNK(info.st_mode) else 'directory' if stat.S_ISDIR(info.st_mode) else 'file'
        return f'{kind}, uid={info.st_uid}, mode={stat.S_IMODE(info.st_mode):04o}'
    except OSError:
        return 'absent or inaccessible'


def main():
    print('Identity bridge inventory; no credential contents, installs or restarts.')
    print(f'Operator uid: {os.getuid()}')
    print('Bundle:', metadata(BUNDLE))
    manifest = BUNDLE / 'manifest.json'
    try:
        info = manifest.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_size > 1048576:
            raise ValueError('Unexpected manifest')
        data = json.loads(manifest.read_text())
        source = data.get('source', '')
        print('Bundle source:', source if isinstance(source, str) and re.fullmatch(r'[a-f0-9]{40}', source) else 'unknown')
    except (OSError, ValueError, TypeError):
        print('Bundle source: unavailable')
    for label, path in [
        ('Dedicated env directory', Path('/home/amir/.config/smartcore-identity')),
        ('Dedicated env file', Path('/home/amir/.config/smartcore-identity/.env')),
        ('Existing private state', BUNDLE / 'dev-secrets/private-stack'),
        ('Existing encrypted key ring', BUNDLE / 'dev-secrets/private-stack/ring'),
    ]:
        print(label + ':', metadata(path))
    for label, binary, arguments, pattern in [
        ('Bundle Node', BUNDLE / 'node/bin/node', ['--version'], r'v\d+\.\d+\.\d+'),
        ('Bundle .NET runtimes', BUNDLE / 'dotnet/dotnet', ['--list-runtimes'], r'Microsoft\.(?:NETCore|AspNetCore)\.App \d+\.\d+\.\d+ \[[A-Za-z0-9_./ -]+\]'),
    ]:
        print(label + ':', command([str(binary), *arguments], pattern))
    for binary in ('node', 'dotnet'):
        path = shutil.which(binary)
        print('System ' + binary + ':', command([path, '--version'], r'v?\d+\.\d+\.\d+') if path else 'unavailable')
    ss = shutil.which('ss')
    if ss:
        try:
            result = subprocess.run([ss, '-H', '-lnt'], capture_output=True, text=True, timeout=8)
            print('Relevant TCP listeners:')
            found = False
            for line in result.stdout.splitlines():
                fields = line.split()
                if len(fields) >= 5 and re.search(r':(?:5157|5171|5172|5181|7443|8443)$', fields[3]):
                    print(' ', fields[3])
                    found = True
            if not found:
                print(' none')
        except (OSError, subprocess.TimeoutExpired):
            print('TCP listener inventory: unavailable')
    for service in ('smartcore-identity-private.service', 'smartcore-identity-gateway.service'):
        print(service + ':', command(['systemctl', 'is-active', service], r'active|inactive|failed|activating|deactivating|unknown'))
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open('https://87.107.54.244:8443/healthz', timeout=8) as response:
            ready = response.status == 200 and response.read(64).strip() == b'smartcore-tls-ready'
        print('Verified TLS transport:', 'PASS' if ready else 'unexpected response')
    except Exception:
        print('Verified TLS transport: unavailable; details suppressed')
    print('No application readiness, account acceptance or Vercel connectivity is claimed.')


if __name__ == '__main__':
    main()
