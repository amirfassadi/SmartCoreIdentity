"""Package already-published Linux x64 hosts and standalone Next; never include secrets."""
import argparse
import hashlib
import json
import re
from pathlib import Path
import shutil
import subprocess
import tarfile

ROOT = Path(__file__).resolve().parents[1]


def package(output, api, bff, node, dotnet):
    if output.exists():
        raise ValueError('Output already exists')
    for binary in (api / 'SmartCore.Identity.Api.dll', bff / 'SmartCore.Identity.Bff.Host.dll', node, dotnet):
        if not binary.is_file():
            raise ValueError('Missing published runtime')
    standalone = ROOT / 'web/identity/.next/standalone'
    if not (standalone / 'server.js').is_file():
        raise ValueError('Missing Next standalone output')
    output.mkdir(parents=True)
    shutil.copytree(api, output / 'api')
    shutil.copytree(bff, output / 'bff')
    shutil.copytree(standalone, output / 'web')
    shutil.copytree(ROOT / 'web/identity/.next/static', output / 'web/.next/static')
    if (ROOT / 'web/identity/public').exists():
        shutil.copytree(ROOT / 'web/identity/public', output / 'web/public')
    (output / 'node/bin').mkdir(parents=True)
    shutil.copy2(node, output / 'node/bin/node')
    node_license = node.resolve().parents[1] / 'LICENSE'
    if node_license.exists():
        shutil.copy2(node_license, output / 'node/LICENSE')
    # Copy only the host and matching .NET/ASP.NET 10 runtime, never an SDK.
    dotnet_root = dotnet.resolve().parent
    frameworks = {}
    runtime_lines = subprocess.check_output([str(dotnet), '--list-runtimes'], text=True)
    for line in runtime_lines.splitlines():
        match = re.fullmatch(r'(Microsoft\.(?:NETCore|AspNetCore)\.App) (10\.0\.\d+) \[(.+)\]', line)
        if match:
            frameworks.setdefault(match[1], {})[match[2]] = Path(match[3]) / match[2]
    versions = set(frameworks.get('Microsoft.NETCore.App', {})) & set(frameworks.get('Microsoft.AspNetCore.App', {}))
    versions = {version for version in versions if (dotnet_root / 'host/fxr' / version).is_dir()}
    if not versions:
        raise ValueError('Matching .NET and ASP.NET 10 runtimes are required')
    runtime = max(versions, key=lambda item: tuple(map(int, item.split('.'))))
    (output / 'dotnet/host').mkdir(parents=True)
    shutil.copy2(dotnet, output / 'dotnet/dotnet')
    shutil.copytree(dotnet_root / 'host/fxr' / runtime, output / 'dotnet/host/fxr' / runtime)
    for name in ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'):
        shutil.copytree(frameworks[name][runtime], output / 'dotnet/shared' / name / runtime)
    for name in ('LICENSE.txt', 'ThirdPartyNotices.txt'):
        if (dotnet_root / name).is_file():
            shutil.copy2(dotnet_root / name, output / 'dotnet' / name)
    for source, destination in [('tools/private_stack.py', 'tools/private_stack.py'),
                                ('deploy/private/run.py', 'run.py'),
                                ('deploy/private/service.py', 'service.py'),
                                ('deploy/private/staging.py', 'staging.py'),
                                ('deploy/gateway/server.mjs', 'deploy/gateway/server.mjs'),
                                ('web/identity/src/lib/bff/bridge.mjs', 'web/identity/src/lib/bff/bridge.mjs'),
                                ('deploy/gateway/nginx-ip-tls.conf', 'deploy/gateway/nginx-ip-tls.conf'),
                                ('deploy/gateway/smartcore-identity-staging.service', 'deploy/gateway/smartcore-identity-staging.service'),
                                ('docs/implementation/RESTRICTED_STAGING.md', 'RESTRICTED_STAGING.md'),
                                ('deploy/development/https-ingress.mjs', 'deploy/development/https-ingress.mjs'),
                                ('docs/implementation/PRIVATE_VPS_BUNDLE.md', 'README.md')]:
        target = output / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / source, target)
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    version = subprocess.check_output([str(node), '--version'], text=True).strip()
    manifest = {'source': source, 'platform': 'linux-x64-glibc', 'capabilities': ['restricted-staging-v1'],
                'build_os': 'ubuntu-22.04', 'test_os': ['ubuntu-22.04', 'debian-12'],
                'postgres_major': 18, 'node': version, 'dotnet': runtime, 'sha256': {}}
    for path in sorted(output.rglob('*')):
        if path.is_file():
            name = path.relative_to(output).as_posix()
            if path.is_symlink() or 'dev-secrets' in path.relative_to(output).parts or any(part.startswith('.env') for part in path.relative_to(output).parts):
                raise ValueError('Unexpected secret/symlink in bundle')
            manifest['sha256'][name] = hashlib.sha256(path.read_bytes()).hexdigest()
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    archive = output.with_suffix('.tar.gz')
    with tarfile.open(archive, 'w:gz') as stream:
        stream.add(output, arcname=output.name)
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_name(archive.name + '.sha256').write_text(checksum + '  ' + archive.name + '\n')
    print(f'Private bundle packaged; source {source}; Node {version}; .NET {runtime}; SHA256 {checksum}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--api', type=Path, required=True)
    parser.add_argument('--bff', type=Path, required=True)
    parser.add_argument('--node', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, required=True)
    arguments = parser.parse_args()
    package(arguments.output.resolve(), arguments.api.resolve(), arguments.bff.resolve(), arguments.node.resolve(), arguments.dotnet.resolve())
