"""Package already-published Linux x64 hosts and standalone Next; never include secrets."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile

ROOT = Path(__file__).resolve().parents[1]


def package(output, api, bff, node):
    if output.exists():
        raise ValueError('Output already exists')
    for binary in (api / 'SmartCore.Identity.Api', bff / 'SmartCore.Identity.Bff.Host', node):
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
    for source, destination in [('tools/private_stack.py', 'tools/private_stack.py'),
                                ('deploy/private/run.py', 'run.py'),
                                ('deploy/development/https-ingress.mjs', 'deploy/development/https-ingress.mjs'),
                                ('docs/implementation/PRIVATE_VPS_BUNDLE.md', 'README.md')]:
        target = output / destination
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / source, target)
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    version = subprocess.check_output([str(node), '--version'], text=True).strip()
    manifest = {'source': source, 'platform': 'ubuntu-22.04-linux-x64', 'node': version, 'sha256': {}}
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
    print(f'Private bundle packaged; source {source}; Node {version}; SHA256 {checksum}')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--api', type=Path, required=True)
    parser.add_argument('--bff', type=Path, required=True)
    parser.add_argument('--node', type=Path, required=True)
    arguments = parser.parse_args()
    package(arguments.output.resolve(), arguments.api.resolve(), arguments.bff.resolve(), arguments.node.resolve())
