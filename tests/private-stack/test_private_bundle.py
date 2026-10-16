import hashlib
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
spec = importlib.util.spec_from_file_location('private_bundle', ROOT / 'deploy/private/run.py')
bundle = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bundle)


class PrivateBundleChecks(unittest.TestCase):
    def test_manifest_accepts_matching_file_and_rejects_tampering_and_traversal(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'runtime').write_bytes(b'verified binary')
            manifest = {'platform': 'ubuntu-22.04-linux-x64', 'sha256':
                        {'runtime': hashlib.sha256(b'verified binary').hexdigest()}}
            (root / 'manifest.json').write_text(json.dumps(manifest))
            with patch.object(bundle, 'ROOT', root):
                bundle.verify()
                (root / 'runtime').write_bytes(b'changed')
                with self.assertRaises(ValueError):
                    bundle.verify()
                manifest['sha256'] = {'../escape': '0' * 64}
                (root / 'manifest.json').write_text(json.dumps(manifest))
                with self.assertRaises(ValueError):
                    bundle.verify()

    def test_database_configuration_cannot_inherit_remote_pg_overrides(self):
        with patch.dict(os.environ, {'PGSERVICE': 'external', 'PGOPTIONS': 'unexpected', 'PGHOST': 'external'}):
            env = bundle.database_env({'db': 'local-test-value'})
        self.assertNotIn('PGSERVICE', env)
        self.assertNotIn('PGOPTIONS', env)
        self.assertEqual(env['PGHOST'], '127.0.0.1')
        self.assertEqual(env['PGPORT'], '5432')
        self.assertEqual(env['PGDATABASE'], 'smartcore_identity_private')

    def test_provisioning_rejects_arbitrary_marker_or_password_sql(self):
        valid = {'project': 'smartcore-private-' + 'a' * 12, 'db': 'b' * 64}
        sql = bundle.provisioning_sql(valid)
        self.assertIn('Private role collision', sql)
        self.assertIn('Private database collision', sql)
        self.assertIn('pg_advisory_lock', sql)
        self.assertNotIn('DROP ', sql)
        for override in ({'project': "unsafe'"}, {'db': "unsafe'"}):
            with self.assertRaises(ValueError):
                bundle.provisioning_sql({**valid, **override})


if __name__ == '__main__':
    unittest.main()
