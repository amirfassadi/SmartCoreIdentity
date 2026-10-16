"""Disposable CI PostgreSQL only: collision refusal and idempotent role/db creation."""
import importlib.util
import os
from pathlib import Path
import secrets
import subprocess

root = Path(os.environ['SMARTCORE_PRIVATE_BUNDLE']).resolve()
spec = importlib.util.spec_from_file_location('private_bundle', root / 'run.py')
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)
bundle.verify()
config = bundle.stack.load()
admin = ['docker', 'exec', '-i', os.environ['CI_PG_CONTAINER'], 'psql', '-U', 'postgres', '-d', 'postgres', '-X', '-v', 'ON_ERROR_STOP=1']

def sql(value):
    return bundle.stack.checked([*admin, '-At'], input=(value + '\n').encode()).decode().strip()

sql('CREATE ROLE smartcore_identity_private NOLOGIN;')
try:
    bundle.provision(config, admin)
except RuntimeError:
    pass
else:
    raise AssertionError('Existing unowned role was adopted')
assert sql("SELECT rolcanlogin FROM pg_roles WHERE rolname='smartcore_identity_private';") == 'f'
sql('DROP ROLE smartcore_identity_private;')
bundle.provision(config, admin)
bundle.provision(config, admin)
assert sql("SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication FROM pg_roles WHERE rolname='smartcore_identity_private';") == 'f'
try:
    bundle.provision({**config, 'project': 'smartcore-private-' + secrets.token_hex(6)}, admin)
except RuntimeError:
    pass
else:
    raise AssertionError('Existing database with different owner marker was adopted')
bundle.check_database(config)
print('Private database provisioning PASS: unrelated role refused/unchanged; dedicated limited role/database created; repeat preserves credentials; foreign marker refused.')
