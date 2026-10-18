"""Install an opt-in systemd service for the existing private, loopback-only bundle."""
import argparse
import os
from pathlib import Path
import pwd
import re
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent
NAME = 'smartcore-identity-private.service'


def unit(user, root, python):
    # Conservative paths keep systemd specifiers, whitespace and shell syntax out.
    if not re.fullmatch(r'[a-z_][a-z0-9_-]*', user) or user == 'root':
        raise ValueError('A non-root application user is required')
    for path in (root, python):
        if not re.fullmatch(r'/[A-Za-z0-9_./-]+', str(path)) or '..' in Path(path).parts:
            raise ValueError('Unsupported service path')
    return f'''[Unit]
Description=SmartCore Identity private test workspace
After=network.target postgresql.service
StartLimitIntervalSec=120
StartLimitBurst=3

[Service]
Type=simple
User={user}
WorkingDirectory={root}
ExecStart={python} {root}/run.py run
Restart=on-failure
RestartSec=10
TimeoutStopSec=40
KillSignal=SIGTERM
KillMode=control-group
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
PrivateDevices=true
ProtectSystem=strict
ProtectHome=read-only
ReadWritePaths={root}/dev-secrets
RestrictSUIDSGID=true
RestrictAddressFamilies=AF_UNIX AF_INET AF_INET6
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
'''


def install():
    if os.getuid() == 0:
        raise ValueError('Run as the existing application user after sudo -v')
    sys.path.insert(0, str(ROOT))
    import run
    run.verify()
    config = run.stack.load()
    run.check_database(config)
    # Refuse to replace an active foreground app. No processes are killed here.
    run.stack.check_ports((5157, 5171, 3181, 7443))
    user = pwd.getpwuid(os.getuid()).pw_name
    content = unit(user, ROOT, Path(sys.executable).resolve())
    with tempfile.TemporaryDirectory(prefix='smartcore-unit-') as temporary:
        path = Path(temporary) / NAME
        path.write_text(content)
        path.chmod(0o600)
        subprocess.run(['sudo', '-n', 'systemd-analyze', 'verify', str(path)], check=True)
        destination = '/etc/systemd/system/' + NAME
        if Path(destination).exists():
            raise ValueError('Service already exists; inspect it before replacing')
        subprocess.run(['sudo', '-n', 'install', '-o', 'root', '-g', 'root', '-m', '644', str(path), destination], check=True)
    subprocess.run(['sudo', '-n', 'systemctl', 'daemon-reload'], check=True)
    subprocess.run(['sudo', '-n', 'systemctl', 'enable', '--now', NAME], check=True)
    print('Private service installed. Check systemctl status smartcore-identity-private.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['install'])
    parser.parse_args()
    try:
        install()
    except Exception as failure:
        print(f'Private service setup failed ({type(failure).__name__}); inspect service status locally.', file=sys.stderr)
        sys.exit(1)
