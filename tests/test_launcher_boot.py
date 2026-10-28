"""Regression checks for the resident launcher's low-memory overwrite failure."""
from pathlib import Path
import contextlib
import io
import struct
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from verify_launcher_boot import check


def fixture():
    elf = bytearray(128)
    elf[:7] = b'\x7fELF\x01\x02\x01'
    struct.pack_into('>HHIIIIIHHHHHH', elf, 16, 2, 20, 1, 0x80003100, 52, 0, 0, 52, 32, 1, 0, 0, 0)
    struct.pack_into('>8I', elf, 52, 1, 96, 0x80003100, 0x80003100, 32, 32, 5, 32)
    elf[96:] = b'\x60\x00\x00\x00' * 8
    dol = bytearray(288)
    struct.pack_into('>I', dol, 0, 256)
    struct.pack_into('>I', dol, 0x48, 0x80003100)
    struct.pack_into('>I', dol, 0x90, 32)
    struct.pack_into('>I', dol, 0xe0, 0x80003100)
    dol[256:] = elf[96:]
    return elf, dol


class BootLayoutTests(unittest.TestCase):
    def test_valid_bootstrap(self):
        with contextlib.redirect_stdout(io.StringIO()):
            check(*fixture())

    def test_elf_header_loaded_over_boot_parameters(self):
        elf, dol = fixture()
        struct.pack_into('>II', elf, 60, 0x80000000, 0x80000000)
        with self.assertRaisesRegex(ValueError, 'low memory'):
            check(elf, dol)

    def test_dol_overwrites_boot_parameters(self):
        elf, dol = fixture()
        struct.pack_into('>I', dol, 0x48, 0x80000000)
        struct.pack_into('>I', dol, 0xe0, 0x80000000)
        with self.assertRaisesRegex(ValueError, 'low-memory'):
            check(elf, dol)

    def test_dol_bss_must_not_clear_vectors(self):
        elf, dol = fixture()
        struct.pack_into('>II', dol, 0xd8, 0x80000000, 32)
        with self.assertRaisesRegex(ValueError, 'BSS'):
            check(elf, dol)

    def test_unaligned_section(self):
        elf, dol = fixture()
        struct.pack_into('>I', dol, 0x90, 31)
        with self.assertRaisesRegex(ValueError, 'aligned'):
            check(elf, dol)

    def test_entry_outside_executable_bytes(self):
        elf, dol = fixture()
        struct.pack_into('>I', elf, 24, 0x80004000)
        with self.assertRaisesRegex(ValueError, 'missing ELF'):
            check(elf, dol)

    def test_truncated_headers_and_payloads(self):
        elf, dol = fixture()
        for image, executable in ((elf[:20], dol), (elf[:80], dol), (elf[:120], dol), (elf, dol[:200]), (elf, dol[:280])):
            with self.subTest(elf=len(image), dol=len(executable)), self.assertRaises(ValueError):
                check(image, executable)

    def test_mismatched_load_bytes(self):
        elf, dol = fixture()
        dol[256] ^= 1
        with self.assertRaisesRegex(ValueError, 'bytes differ'):
            check(elf, dol)

    def test_excess_zero_padding_must_not_expand_load_memory(self):
        elf, dol = fixture()
        dol.extend(bytes(32))
        struct.pack_into('>I', dol, 0x90, 64)
        with self.assertRaisesRegex(ValueError, 'padding'):
            check(elf, dol)


if __name__ == '__main__':
    unittest.main()
