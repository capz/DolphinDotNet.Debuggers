"""A release must contain the same complete, intact bootable console bundle."""
import hashlib
import contextlib
import io
import json
from pathlib import Path
import sys
import struct
import tempfile
import unittest
from unittest.mock import patch
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from dol_sections import pad
from package_launcher import FILES, stage, verify_bundle
from release_assets import collect_assets
from test_dol_sections import fixture
from test_launcher_boot import fixture as boot_fixture


class LauncherPackagingTests(unittest.TestCase):
    def bundle(self):
        dol = pad(fixture())
        struct.pack_into('>II', dol, 0xd8, 0, 0)  # Stackless bootstrap has no BSS.
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

    def test_matching_checksum_cannot_hide_unsafe_boot_layout(self):
        for address in (0x80000000, 0x81600000):
            files = self.bundle()
            dol = bytearray(files[FILES[0]])
            struct.pack_into('>I', dol, 0x48, address)
            struct.pack_into('>I', dol, 0xe0, address)
            files[FILES[0]] = dol
            manifest = json.loads(files['build-info.json'])
            manifest['sha256'] = hashlib.sha256(dol).hexdigest()
            files['build-info.json'] = json.dumps(manifest).encode()
            with self.subTest(address=hex(address)), self.assertRaisesRegex(ValueError, 'low-memory'):
                verify_bundle(files.__getitem__)

    def test_staging_copies_and_hashes_the_validated_snapshot(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            elf, dol = boot_fixture()
            (root / 'DolphinDotNet.Launcher.elf').write_bytes(elf)
            source = root / FILES[0]
            source.write_bytes(dol)

            def replace_source(*args, **kwargs):
                source.write_bytes(b'replaced during packaging')
                return '0123456789abcdef\n'

            with patch('package_launcher.subprocess.check_output', side_effect=replace_source):
                with contextlib.redirect_stdout(io.StringIO()):
                    stage(root / 'staged', source=root)
            self.assertEqual((root / 'staged' / FILES[0]).read_bytes(), dol)
            manifest = json.loads((root / 'staged' / 'build-info.json').read_text())
            self.assertEqual(manifest['sha256'], hashlib.sha256(dol).hexdigest())


if __name__ == '__main__':
    unittest.main()
