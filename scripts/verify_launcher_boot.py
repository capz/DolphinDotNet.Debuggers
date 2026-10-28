"""Reject launcher ELF/DOL segments that overwrite GameCube boot parameters."""
import argparse
from pathlib import Path
import struct

from dol_sections import sections

BOOT_START = 0x80003100
RESIDENT_START = 0x81600000


def check_dol(dol):
    """Validate a packaged bootstrap even when its ELF is not included."""
    if len(dol) < 256:
        raise ValueError('truncated DOL header')
    if struct.unpack_from('>I', dol, 0xdc)[0]:
        raise ValueError('stackless bootstrap must not clear a BSS range')
    result = sections(dol, aligned=True)
    for _, _, address, size in result:
        if address < BOOT_START or address + size > RESIDENT_START:
            raise ValueError('DOL overwrites low-memory boot parameters or resident memory')
    return result


def check(elf, dol):
    if len(elf) < 52 or elf[:7] != b'\x7fELF\x01\x02\x01':
        raise ValueError('not a big-endian ELF32 bootstrap')
    kind, machine, version, entry = struct.unpack_from('>HHII', elf, 16)
    if (kind, machine, version) != (2, 20, 1) or entry & 3:
        raise ValueError('bootstrap must be fixed-address PowerPC code with an aligned entry')
    table = struct.unpack_from('>I', elf, 28)[0]
    stride, count = struct.unpack_from('>HH', elf, 42)
    if (stride != 32 or not 1 <= count <= 64 or table < 52 or
            table > len(elf) or count > (len(elf) - table) // stride):
        raise ValueError('invalid ELF program-header table')
    loads = []
    executable = False
    for i in range(count):
        kind, offset, address, physical, files, memory, flags, _ = struct.unpack_from(
            '>8I', elf, table + i * stride)
        if kind in (2, 3):
            raise ValueError('dynamic bootstrap is unsupported')
        if kind == 1 and memory:
            if files != memory or physical not in (0, address):
                raise ValueError('bootstrap segments must be file-backed with matching addresses')
            if address < BOOT_START or address + memory > RESIDENT_START:
                raise ValueError('ELF load segment overwrites low memory or resident memory')
            if offset + files > len(elf):
                raise ValueError('truncated ELF segment')
            if any(address < a + n and a < address + memory for a, _, n in loads):
                raise ValueError('overlapping ELF load segments')
            executable |= bool(flags & 1 and address <= entry < address + files)
            loads.append((address, elf[offset:offset + files], memory))
    if not loads or not executable:
        raise ValueError('missing ELF load segments')
    dol_sections = check_dol(dol)
    if len(dol_sections) != len(loads):
        raise ValueError('ELF/DOL segment count mismatch')
    for (address, data, _), (_, offset, dol_address, size) in zip(loads, dol_sections):
        output = dol[offset:offset + size]
        if size != ((len(data) + 31) & ~31):
            raise ValueError('DOL contains more than alignment padding beyond its ELF segment')
        if address != dol_address or output[:len(data)] != data or any(output[len(data):]):
            raise ValueError('ELF/DOL load bytes differ')
    if struct.unpack_from('>I', dol, 0xe0)[0] != entry:
        raise ValueError('ELF/DOL entry mismatch')
    print('Bootstrap ELF/DOL: boot parameters preserved, matching bytes, aligned sections.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('elf', type=Path)
    parser.add_argument('dol', type=Path)
    args = parser.parse_args()
    check(args.elf.read_bytes(), args.dol.read_bytes())
