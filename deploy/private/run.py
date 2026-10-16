"""Ubuntu 22.04 x64 private bundle; Python 3.10+, no SDK/npm/Docker required."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'tools'))
import private_stack as stack

DATABASE = 'smartcore_identity_private'


def checksum(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def verify():
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        raise ValueError('Linux x86_64 required')
    manifest = json.loads((ROOT / 'manifest.json').read_text())
    if manifest.get('platform') != 'ubuntu-22.04-linux-x64':
        raise ValueError('Unsupported bundle target')
    for name, expected in manifest['sha256'].items():
        path = ROOT / name
        if Path(name).is_absolute() or '..' in Path(name).parts or not name:
            raise ValueError('Invalid bundle path')
        if any(parent.is_symlink() for parent in (path, *path.parents)):
            raise ValueError('Bundle symlink rejected')
        if checksum(path) != expected:
            raise ValueError('Bundle checksum mismatch')
    # Verify against the separately published archive SHA-256 before extraction.
    # This manifest detects corruption/drift, not an attacker replacing the manifest.
    print('Bundle file hashes verified.', flush=True)


def database_env(config):
    env = {key: value for key, value in stack.base_env().items() if not key.upper().startswith('PG')}
    return {**env, 'PGHOST': '127.0.0.1', 'PGPORT': '5432', 'PGDATABASE': DATABASE,
            'PGUSER': DATABASE, 'PGPASSWORD': config['db'], 'PGCONNECT_TIMEOUT': '5'}


def query(config, sql):
    return stack.checked(['psql', '-X', '-At', '-v', 'ON_ERROR_STOP=1'],
                         env=database_env(config), input=(sql + '\n').encode())


def check_database(config):
    version = int(query(config, 'SHOW server_version_num;').strip())
    if version < 170000 or version >= 180000:
        raise ValueError('PostgreSQL 17 required')
    # A similarly named pre-existing database is never silently adopted.
    result = query(config, "SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database();").decode().strip()
    if result != config['project']:
        raise ValueError('Database ownership marker mismatch')


def provisioning_sql(config):
    marker = config['project']
    password = config['db']
    # These values are generated locally, never arbitrary user-provided SQL.
    if not re.fullmatch(r'smartcore-private-[a-f0-9]{12}', marker) or not re.fullmatch(r'[a-f0-9]{64}', password):
        raise ValueError('Invalid provisioning material')
    return rf"""
SELECT pg_advisory_lock(641017);
DO $guard$
BEGIN
 IF current_setting('server_version_num')::int NOT BETWEEN 170000 AND 179999 THEN
   RAISE EXCEPTION 'PostgreSQL 17 required';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='{DATABASE}' AND
   (shobj_description(oid,'pg_authid') IS DISTINCT FROM '{marker}' OR rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication)) THEN
   RAISE EXCEPTION 'Private role collision';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_database WHERE datname='{DATABASE}' AND
   (shobj_description(oid,'pg_database') IS DISTINCT FROM '{marker}' OR pg_get_userbyid(datdba)<>'{DATABASE}')) THEN
   RAISE EXCEPTION 'Private database collision';
 END IF;
END $guard$;
SELECT 'CREATE ROLE {DATABASE} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD ''{password}'''
 WHERE NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='{DATABASE}')
\gexec
COMMENT ON ROLE {DATABASE} IS '{marker}';
SELECT 'CREATE DATABASE {DATABASE} OWNER {DATABASE} TEMPLATE template0 ENCODING ''UTF8'''
 WHERE NOT EXISTS(SELECT 1 FROM pg_database WHERE datname='{DATABASE}')
\gexec
COMMENT ON DATABASE {DATABASE} IS '{marker}';
REVOKE ALL ON DATABASE {DATABASE} FROM PUBLIC;
SELECT pg_advisory_unlock(641017);
"""


def provision(config, admin_command=None):
    # Run `sudo -v` interactively first. No password or SQL is printed or put in argv.
    command = admin_command or ['sudo', '-n', '-u', 'postgres', 'psql', '-h', '/var/run/postgresql',
                                '-p', '5432', '-d', 'postgres', '-X', '-v', 'ON_ERROR_STOP=1']
    with stack.lock():
        stack.checked(command, input=provisioning_sql(config).encode())
        check_database(config)
    print('Dedicated private role/database provisioned; PostgreSQL configuration preserved.')


def run(config):
    connection = f"Host=127.0.0.1;Port=5432;Database={DATABASE};Username={DATABASE};Password={config['db']}"
    identity, bff = stack.environments(config, connection)
    # Limit processor heuristics for this small private workspace, not public load.
    identity['DOTNET_PROCESSOR_COUNT'] = '1'
    bff['DOTNET_PROCESSOR_COUNT'] = '1'
    node = str(ROOT / 'node/bin/node')
    stack.serve(config, identity, bff, [str(ROOT / 'api/SmartCore.Identity.Api')],
                [str(ROOT / 'bff/SmartCore.Identity.Bff.Host')],
                [node, str(ROOT / 'web/server.js')], node=node,
                prepare_database=lambda: check_database(config), bff_ports=(5171,))


def main():
    os.umask(0o077)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=('verify', 'init', 'provision-db', 'run', 'inbox'))
    parser.add_argument('--kind', choices=('registration', 'setup', 'reset'), default='registration')
    args = parser.parse_args()
    try:
        verify()
        if args.command == 'verify':
            return 0
        if args.command == 'init':
            stack.initialize()
            return 0
        config = stack.load()
        if args.command == 'provision-db':
            provision(config)
        elif args.command == 'run':
            run(config)
        elif args.command == 'inbox':
            contact = input('Private test email or E.164 mobile: ')
            print(stack.inbox(args.kind, contact, query_runner=lambda sql: query(config, sql)))
    except KeyboardInterrupt:
        return 0
    except Exception as error:
        print(f'Private bundle failed during {args.command} ({type(error).__name__}); raw diagnostics suppressed.', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
