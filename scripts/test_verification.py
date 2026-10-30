import hashlib
from pathlib import Path
import tempfile
import unittest

from verify_repo import AUTHORS, ROOT, check, verify_hashes, verify_identity


class ReleaseChecks(unittest.TestCase):
    def test_actual_release(self):
        self.assertEqual(check(ROOT), '2.1.4')

    def test_valid_digest(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'sample').write_bytes(b'fixture')
            verify_hashes(root, {'sample': hashlib.sha256(b'fixture').hexdigest().upper()})

    def test_changed_digest(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'sample').write_bytes(b'changed')
            with self.assertRaises(ValueError):
                verify_hashes(root, {'sample': hashlib.sha256(b'fixture').hexdigest()})

    def test_missing_file(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(FileNotFoundError):
                verify_hashes(Path(folder), {'missing': '0'*64})

    def test_paths_stay_inside_repo(self):
        for name in ['../outside', '/outside', 'C:/outside', 'folder\\outside']:
            with self.subTest(name=name), self.assertRaises(ValueError):
                verify_hashes(ROOT, {name: '0'*64})

    def test_version_and_credits(self):
        source = f'const string Version="2.1.3";const string AssemblyVersion="2.1.3.0";const string Authors="{AUTHORS}";'
        verify_identity(source, '2.1.3')
        for old, new in [('2.1.3.0', '2.1.2.0'), ('2.1.3"', '2.1.2"'), (AUTHORS, 'Wrong author')]:
            with self.subTest(old=old), self.assertRaises(ValueError):
                verify_identity(source.replace(old, new), '2.1.3')


if __name__ == '__main__':
    unittest.main()
