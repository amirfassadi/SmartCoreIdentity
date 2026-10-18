import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools'))
spec = importlib.util.spec_from_file_location('restricted_staging', ROOT / 'deploy/private/staging.py')
staging = importlib.util.module_from_spec(spec)
spec.loader.exec_module(staging)
SETTINGS = {'SMARTCORE_BFF_BRIDGE_ENABLED': 'false',
            'SMARTCORE_BROWSER_ORIGIN': 'https://accounts.example.test',
            'SMARTCORE_BFF_GATEWAY_ORIGIN': 'https://87.107.54.244:8443',
            'SMARTCORE_BFF_GATEWAY_KEY': 'K' * 43}


class StagingChecks(unittest.TestCase):
    def test_env_is_private_plain_data_and_disabled_flags_stay_disabled(self):
        with tempfile.TemporaryDirectory() as temporary:
            parent = Path(temporary)
            file = parent / '.env'
            valid = '\n'.join(key + '=' + value for key, value in SETTINGS.items()) + '\n'
            file.write_text(valid)
            file.chmod(0o600)
            self.assertEqual(staging.read_settings(file), SETTINGS)
            for suffix in ['SMARTCORE_BFF_BRIDGE_ENABLED=true\n', 'EXEC=$(touch /never)\n',
                           'EXEC="value"\n', 'NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED=true\n', 'x' * 16385]:
                file.write_text(valid + suffix)
                with self.subTest(suffix=suffix[:30]), self.assertRaises(ValueError):
                    staging.read_settings(file)
            file.write_text(valid)
            file.chmod(0o644)
            with self.assertRaises(ValueError):
                staging.read_settings(file)
            file.chmod(0o600)
            link = parent / 'link'; link.symlink_to(file)
            with self.assertRaises(ValueError):
                staging.read_settings(link)
            parent.chmod(0o755)
            with self.assertRaises(ValueError):
                staging.read_settings(file)

    def test_origins_reject_loose_authority_and_local_targets(self):
        self.assertEqual(staging.fixed_origin('https://87.107.54.244:8443/'), SETTINGS['SMARTCORE_BFF_GATEWAY_ORIGIN'])
        for value in ['http://public.example.test', 'https://127.0.0.1', 'https://10.10.34.36', 'https://[::1]',
                      'https://nested.localhost', 'https://*.example.test', 'https://example.test/path',
                      'https://user:pass@example.test', 'https://example.test?query', 'https://example.test#fragment',
                      'https://example.test:99999', 'https://example.test.', 'https://REPLACE_WITH_BACKEND_HOSTNAME']:
            with self.subTest(origin=value), self.assertRaises(ValueError):
                staging.fixed_origin(value)
        with self.assertRaises(ValueError):
            staging.fixed_origin('https://87.107.54.244', browser=True)

    def test_existing_keys_are_preserved_and_gateway_proof_matches_node_vector(self):
        keys = ('db', 'client', 'receipt', 'mac', 'material', 'signing', 'inbox', 'operator', 'session_mac', 'session_material', 'pfx')
        config = {key: 'synthetic-' + key for key in keys}
        before = config.copy()
        with patch.dict(os.environ, {'Bff__AccountsEnabled': 'true', 'ASPNETCORE_ENVIRONMENT': 'Production',
                                    'SMARTCORE_BFF_ORIGINS': 'http://public.example.test'}):
            identity, bff = staging.environments(config, SETTINGS)
        self.assertEqual(config, before)
        self.assertEqual(bff['ASPNETCORE_ENVIRONMENT'], 'RestrictedStaging')
        self.assertEqual(bff['Bff__ExternalOrigin'], SETTINGS['SMARTCORE_BROWSER_ORIGIN'])
        self.assertEqual(bff['Bff__GatewayProof'], 'sAQ3WUvDG_ZZwIMe1R7WldUTaIHF0wauMliwEnuMhYs')
        self.assertEqual(bff['Bff__ClientKey'], config['client'])
        self.assertEqual(bff['Bff__SessionMacKey'], config['session_mac'])
        self.assertEqual(identity['Identity__AccessSigningKey'], config['signing'])
        for key in ('AccountsEnabled', 'RegistrationEnabled', 'RecoveryEnabled'):
            self.assertEqual(bff['Bff__' + key], 'false')
        self.assertNotIn('SMARTCORE_BFF_ORIGINS', bff)

    def test_disabled_activation_and_occupied_ports_cannot_touch_database(self):
        with patch.object(staging.bundle, 'check_database') as database:
            with self.assertRaises(ValueError):
                staging.run({}, SETTINGS, Path('/unused'))
            database.assert_not_called()
        config = {key: 'synthetic-' + key for key in ('db', 'client', 'receipt', 'mac', 'material', 'signing',
            'inbox', 'operator', 'session_mac', 'session_material', 'pfx')}
        from contextlib import nullcontext
        with patch.object(staging.stack, 'lock', return_value=nullcontext()), \
                patch.object(staging.stack, 'check_ports', side_effect=OSError), \
                patch.object(staging.bundle, 'check_database') as database, \
                patch.object(staging.subprocess, 'Popen') as spawn:
            with self.assertRaises(OSError):
                staging.run(config, {**SETTINGS, 'SMARTCORE_BFF_BRIDGE_ENABLED': 'true'}, Path('/unused'))
            database.assert_not_called(); spawn.assert_not_called()

    def test_old_bundle_is_not_silently_upgraded_by_copying_runner(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(staging, 'ROOT', Path(temporary)), \
                patch.object(staging.bundle, 'verify'):
            manifest = Path(temporary) / 'manifest.json'
            manifest.write_text(json.dumps({'sha256': {}}))
            with self.assertRaises(ValueError):
                staging.verify_capability()
            manifest.write_text(json.dumps({'capabilities': ['restricted-staging-v1'], 'sha256': {
                'staging.py': '', 'deploy/gateway/server.mjs': '', 'web/identity/src/lib/bff/bridge.mjs': ''}}))
            staging.verify_capability()

    def test_systemd_template_is_valid_and_only_private_state_is_writable(self):
        template = ROOT / 'deploy/gateway/smartcore-identity-staging.service'
        result = subprocess.run(['systemd-analyze', 'verify', str(template)], capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr.decode())
        text = template.read_text()
        self.assertIn('User=amir\n', text)
        self.assertIn('KillMode=control-group\n', text)
        self.assertIn('ProtectHome=read-only\n', text)
        self.assertIn('ReadWritePaths=/home/amir/smartcore-identity-private/dev-secrets\n', text)
        self.assertNotIn('Environment=', text)

    def test_failed_start_and_signals_reap_only_owned_process_groups(self):
        unrelated = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], start_new_session=True)
        try:
            for interrupt in (False, True):
                child = None
                try:
                    with staging.children() as owned:
                        child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'], start_new_session=True)
                        owned.append(child)
                        if interrupt:
                            os.kill(os.getpid(), signal.SIGTERM)
                        else:
                            raise RuntimeError('synthetic child failure')
                except (RuntimeError, KeyboardInterrupt):
                    pass
                self.assertIsNotNone(child)
                self.assertIsNotNone(child.poll())
                self.assertIsNone(unrelated.poll())
        finally:
            unrelated.terminate(); unrelated.wait(timeout=5)


if __name__ == '__main__':
    unittest.main()
