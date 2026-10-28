"""Reject launcher ELF/DOL segments that overwrite GameCube boot parameters."""
import argparse
from pathlib import Path
import struct


def check(elf, dol):
    if len(elf) < 52 or elf[:7] != b'\x7fELF\x01\x02\x01':
        raise ValueError('not a big-endian ELF32 bootstrap')
    kind, machine, version, entry = struct.unpack_from('>HHII', elf, 16)
    if (kind, machine, version) != (2, 20, 1) or entry & 3:
        raise ValueError('bootstrap must be fixed-address PowerPC code with an aligned entry')
    table = struct.unpack_from('>I', elf, 28)[0]
    stride, count = struct.unpack_from('>HH', elf, 42)
    if stride != 32 or not 1 <= count <= 64 or table < 52 or table > len(elf) or count > (len(elf) - table) // stride:
        raise ValueError('invalid ELF program-header table')
    loads = []
    executable = False
    for i in range(count):
        kind, offset, address, physical, files, memory, flags, align = struct.unpack_from('>8I', elf, table + i * stride)
        if kind in (2, 3):
            raise ValueError('dynamic bootstrap is unsupported')
        if kind == 1 and memory:
            if files != memory or physical not in (0, address):
                raise ValueError('bootstrap segments must be file-backed with matching addresses')
            if address < 0x80003100 or address + memory > 0x81600000:
                raise ValueError('ELF load segment overwrites low memory or resident memory')
            if offset + files > len(elf):
                raise ValueError('truncated ELF segment')
            if any(address < a + n and a < address + memory for a, _, n in loads):
                raise ValueError('overlapping ELF load segments')
            executable |= bool(flags & 1 and address <= entry < address + files)
            loads.append((address, elf[offset:offset + files], memory))
    if not loads or not executable:
        raise ValueError('missing ELF load segments')
    sections = []
    if len(dol) < 256:
        raise ValueError('truncated DOL')
    bss_address, bss_size = struct.unpack_from('>II', dol, 0xd8)
    if bss_size:
        raise ValueError('stackless bootstrap must not clear a BSS range')
    for i in range(18):
        offset, address, size = (struct.unpack_from('>I', dol, field + 4 * i)[0] for field in (0, 0x48, 0x90))
        if not size:
            continue
        if address < 0x80003100 or address + size > 0x81600000:
            raise ValueError('DOL overwrites low-memory boot parameters or resident memory')
        if offset < 256 or offset + size > len(dol) or (offset | address | size) & 31:
            raise ValueError('DOL section is truncated or not 32-byte aligned')
        if any(address < a + len(data) and a < address + size for a, data in sections):
            raise ValueError('overlapping DOL load sections')
        sections.append((address, dol[offset:offset + size]))
    if len(sections) != len(loads):
        raise ValueError('ELF/DOL segment count mismatch')
    for (ea, data, memory), (da, output) in zip(loads, sections):
        if ea != da or output[:len(data)] != data or any(output[len(data):]):
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
