import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

SOURCE = Path(__file__).resolve().parents[2] / 'deploy/private/service.py'
spec = importlib.util.spec_from_file_location('private_service', SOURCE)
service = importlib.util.module_from_spec(spec)
spec.loader.exec_module(service)


class PrivateServiceChecks(unittest.TestCase):
    def test_systemd_accepts_service_and_it_does_not_run_as_root(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'dev-secrets').mkdir()
            path = root / 'smartcore-identity-private.service'
            path.write_text(service.unit('amir', root, Path('/usr/bin/python3')))
            result = subprocess.run(['systemd-analyze', 'verify', str(path)], capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr.decode())
            self.assertIn('User=amir\n', path.read_text())
            self.assertIn('KillMode=control-group\n', path.read_text())

    def test_root_and_unit_directive_injection_are_rejected(self):
        for user in ['root', 'amir\nUser=root', 'amir%u']:
            with self.subTest(user=user), self.assertRaises(ValueError):
                service.unit(user, Path('/home/amir/app'), Path('/usr/bin/python3'))
        for root in ['/home/amir/a b', '/home/amir/%i', '/home/amir/../root', '/home/amir/app\nUser=root']:
            with self.subTest(root=root), self.assertRaises(ValueError):
                service.unit('amir', Path(root), Path('/usr/bin/python3'))

    def test_root_install_refuses_before_any_subprocess(self):
        with patch.object(service.os, 'getuid', return_value=0), patch.object(service.subprocess, 'run') as command:
            with self.assertRaises(ValueError):
                service.install()
            command.assert_not_called()
