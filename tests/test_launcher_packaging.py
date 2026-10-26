"""A release must contain the same complete, intact bootable console bundle."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from dol_sections import pad
from package_launcher import FILES, verify_bundle
from release_assets import collect_assets
from test_dol_sections import fixture


class LauncherPackagingTests(unittest.TestCase):
    def bundle(self):
        dol = pad(fixture())
        return dict(zip(FILES, (dol, b'Boot instructions', b'License', json.dumps(dict(
            repository='capz/DolphinDotNet.Debuggers', resident=True,
            sha256=hashlib.sha256(dol).hexdigest())).encode())))

    def test_release_publishes_exact_dol_and_complete_standalone_archive(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            files = self.bundle()
            for name, content in files.items():
                (root / name).write_bytes(content)
            assets = collect_assets(launcher_dir=root)
            self.assertEqual([FILES[0], 'DolphinDotNet.Launcher.zip'], [p.name for p in assets])
            with ZipFile(assets[1]) as archive:
                self.assertEqual(set(FILES), set(archive.namelist()))
                verify_bundle(archive.read)
                self.assertEqual(files[FILES[0]], archive.read(FILES[0]))

    def test_missing_file_corruption_and_invalid_dol_fail_verification(self):
        for name in FILES:
            files = self.bundle()
            del files[name]
            with self.subTest(missing=name), self.assertRaises(KeyError):
                verify_bundle(files.__getitem__)
        files = self.bundle()
        files[FILES[0]] += b'corrupted'
        with self.assertRaisesRegex(ValueError, 'checksum'):
            verify_bundle(files.__getitem__)
        files = self.bundle()
        files[FILES[0]] = b'not a DOL'
        manifest = json.loads(files['build-info.json'])
        manifest['sha256'] = hashlib.sha256(files[FILES[0]]).hexdigest()
        files['build-info.json'] = json.dumps(manifest).encode()
        with self.assertRaisesRegex(ValueError, 'header'):
            verify_bundle(files.__getitem__)


if __name__ == '__main__':
    unittest.main()
