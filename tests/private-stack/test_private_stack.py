import importlib.util
import os
from pathlib import Path
import tempfile
import socket
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('private_stack', Path(__file__).resolve().parents[2] / 'tools/private_stack.py')
stack = importlib.util.module_from_spec(spec)
spec.loader.exec_module(stack)


class PrivateStackChecks(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.state = self.root / 'dev-secrets/private-stack'
        self.patches = [patch.object(stack, 'ROOT', self.root), patch.object(stack, 'STATE', self.state)]
        for item in self.patches:
            item.start()
            self.addCleanup(item.stop)

    def initialize(self):
        # Actual OpenSSL generation, not an imitation of key persistence.
        stack.initialize()
        return stack.load()

    def test_existing_keys_are_preserved(self):
        first = self.initialize()
        stack.initialize()
        self.assertEqual(stack.load(), first)
        self.assertEqual((self.state / 'configuration.json').stat().st_mode & 0o777, 0o600)
        self.assertNotEqual(first['mac'], first['material'])
        self.assertNotEqual(first['client'], first['receipt'])

    def test_public_or_symlinked_secret_is_rejected(self):
        self.initialize()
        path = self.state / 'configuration.json'
        path.chmod(0o644)
        with self.assertRaises(ValueError):
            stack.load()
        path.chmod(0o600)
        path.rename(self.state / 'saved.json')
        path.symlink_to(self.state / 'saved.json')
        with self.assertRaises(ValueError):
            stack.load()

    def test_incomplete_workspace_does_not_replace_material(self):
        self.state.mkdir(parents=True, mode=0o700)
        self.state.parent.chmod(0o700)
        (self.state / 'existing-key').write_text('preserve')
        with self.assertRaises(ValueError):
            stack.initialize()
        self.assertEqual((self.state / 'existing-key').read_text(), 'preserve')

    def test_runtime_overrides_do_not_escape_loopback(self):
        config = self.initialize()
        with patch.dict(os.environ, {'ASPNETCORE_URLS': 'http://0.0.0.0:80',
                    'Bff__IdentityOrigin': 'https://external.invalid', 'Identity__WorkerEnabled': 'false',
                    'ConnectionStrings__Bff': 'external', 'SMARTCORE_TLS_KEY': 'external',
                    'NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED': 'false'}):
            identity, bff = stack.environments(config)
        self.assertEqual(identity['ASPNETCORE_URLS'], 'http://127.0.0.1:5157')
        self.assertEqual(bff['Bff__IdentityOrigin'], 'http://127.0.0.1:5157')
        self.assertIn('Port=55437;', bff['ConnectionStrings__Bff'])
        self.assertNotIn('SMARTCORE_TLS_KEY', identity)
        self.assertNotIn('NEXT_PUBLIC_BFF_ACCOUNTS_ENABLED', bff)

    def test_second_runner_is_rejected(self):
        self.initialize()
        with stack.lock():
            with self.assertRaises(BlockingIOError):
                with stack.lock():
                    self.fail('Second runner must not acquire the lock')

    def test_command_failure_does_not_include_secret_output(self):
        with self.assertRaises(RuntimeError) as error:
            stack.checked(['python', '-c', 'import sys; print("proof-value"); sys.exit(1)'])
        self.assertNotIn('proof-value', str(error.exception))

    def test_restart_allows_time_wait_but_rejects_active_listener(self):
        with socket.socket() as listener:
            listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            listener.bind(('127.0.0.1', 0))
            port = listener.getsockname()[1]
            listener.listen()
            with self.assertRaises(OSError):
                stack.check_ports((port,))
            with socket.create_connection(('127.0.0.1', port)) as client:
                peer, _ = listener.accept()
                peer.close()  # Server performs the active close and enters TIME_WAIT.
                self.assertEqual(client.recv(1), b'')
        stack.check_ports((port,))


if __name__ == '__main__':
    unittest.main()
