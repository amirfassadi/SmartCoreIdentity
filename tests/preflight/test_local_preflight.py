"""Regression controls for redaction and false-positive/false-PASS boundaries."""
import contextlib
import importlib.util
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('preflight_local', ROOT / 'tools/preflight_local.py')
helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)


class LocalPreflightChecks(unittest.TestCase):
    def test_every_cookie_attribute_and_field_is_checked(self):
        good = '__Host-smartcore-antiforgery=fixture; Secure; HttpOnly; SameSite=Lax; Path=/'
        helper.cookie_policy([good])
        for fields in [[], [good, good], [good.replace('HttpOnly;', '')], [good.replace('Path=/', 'Path=/other')], [good + '; Domain=localhost'], [good.replace('SameSite=Lax', 'SameSite=None')], [good.replace('Secure;', '')], [good.replace('antiforgery', 'session')]]:
            with self.assertRaises(ValueError): helper.cookie_policy(fields)

    def test_schema_identifier_scan_is_case_insensitive_and_ignores_description(self):
        import yaml
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch); (root / 'contracts').mkdir(); (root / 'web/identity/src').mkdir(parents=True)
            path = root / 'contracts/bff-browser.openapi.yaml'
            for name in ('personId', 'PersonId', 'person_id', 'PERSON_ID'):
                path.write_text(yaml.safe_dump({'components': {'schemas': {'Person': {'properties': {name: {'type':'string'}}}}}}))
                with self.assertRaises(ValueError): helper.browser_boundary(root)
            path.write_text(yaml.safe_dump({'description':'personId must not appear', 'properties':{'displayName':{'type':'string'}}}))
            with contextlib.redirect_stdout(io.StringIO()): helper.browser_boundary(root)
            source = root / 'web/identity/src/example.ts'
            source.write_text('const profile = {PersonId: "fixture"};')
            with self.assertRaises(ValueError): helper.browser_boundary(root)

    def test_current_worktree_scan_includes_untracked_and_redacts_values(self):
        with tempfile.TemporaryDirectory() as scratch:
            root = Path(scratch)
            subprocess.run(['git','init','-q',str(root)], check=True)
            value = 'AKIA' + 'A' * 16  # Synthetic marker, never an issued key.
            (root / 'untracked.txt').write_text(value)
            output = io.StringIO()
            with contextlib.redirect_stdout(output), self.assertRaises(ValueError): helper.heuristic(root)
            self.assertTrue(value not in output.getvalue(), 'Source values must not be printed')
            self.assertIn('untracked.txt:1', output.getvalue())

    def test_copy_uses_uncommitted_content_and_rejects_external_symlink(self):
        with tempfile.TemporaryDirectory() as scratch:
            base = Path(scratch); root = base / 'repo'; root.mkdir()
            subprocess.run(['git','init','-q',str(root)], check=True)
            source = root / 'tracked.txt'; source.write_text('old')
            subprocess.run(['git','-C',str(root),'add','tracked.txt'], check=True)
            source.write_text('new'); (root / 'untracked.txt').write_text('current')
            with contextlib.redirect_stdout(io.StringIO()): helper.copy_worktree(root, base / 'copy')
            self.assertEqual((base / 'copy/tracked.txt').read_text(), 'new')
            self.assertEqual((base / 'copy/untracked.txt').read_text(), 'current')
            (root / 'external').symlink_to(base / 'copy/tracked.txt')
            with self.assertRaises(ValueError): helper.copy_worktree(root, base / 'copy2')
            with self.assertRaises(ValueError): helper.copy_worktree(root, root / 'inside')

    def test_remote_url_is_rejected_before_any_probe(self):
        for origin in ('https://example.test', 'http://localhost:7443', 'https://user:password@localhost:7443', 'https://localhost:7443/path'):
            with self.assertRaises(ValueError): helper.probe(origin, None)

    def test_relative_tmpdir_becomes_absolute_and_is_cleaned(self):
        with tempfile.TemporaryDirectory() as scratch:
            base=Path(scratch); (base / 'relative').mkdir()
            captured=base / 'captured'; scanner=base / 'scanner'
            scanner.write_text('#!/usr/bin/env bash\nif [[ "$1" == dir ]]; then printf "%s" "$2" > "$PREFLIGHT_CAPTURE"; fi\nexit 0\n')
            scanner.chmod(0o700)
            result=subprocess.run(['bash',str(ROOT / 'preflight-local.sh'),'--static-only'],cwd=base,
                env={**os.environ,'SMARTCORE_PYTHON':sys.executable,'SMARTCORE_GITLEAKS':str(scanner),'PREFLIGHT_CAPTURE':str(captured),'TMPDIR':'relative'},capture_output=True,text=True,timeout=45)
            self.assertEqual(result.returncode,2)
            self.assertTrue(Path(captured.read_text()).is_absolute())
            self.assertEqual(list((base / 'relative').iterdir()),[])

    def test_static_mode_never_marks_skips_as_success(self):
        with tempfile.TemporaryDirectory() as scratch:
            result = subprocess.run(['bash',str(ROOT / 'preflight-local.sh'),'--static-only'],env={**os.environ,'SMARTCORE_PYTHON':sys.executable,'SMARTCORE_GITLEAKS':'/nonexistent/preflight-gitleaks','TMPDIR':scratch}, capture_output=True,text=True,timeout=45)
            self.assertEqual(result.returncode, 2)
            self.assertIn('RESULT: INCOMPLETE', result.stdout)
            self.assertNotIn('requested local B1 checks passed', result.stdout)


if __name__ == '__main__': unittest.main()
