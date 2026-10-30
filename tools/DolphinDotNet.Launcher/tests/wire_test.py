#!/usr/bin/env python3
"""Independent Python protocol vectors against the production C receiver core."""
import argparse
from pathlib import Path
import struct
import subprocess
import zlib


def frame(kind, value=0, detail=0):
    return struct.pack('>4sHHII', b'DNDL', 1, kind, value, detail)


def fixture():
    elf = bytearray(256)
    elf[:7] = b'\x7fELF\1\2\1'
    struct.pack_into('>HHIIIIIHHHHHH', elf, 16, 2, 20, 1, 0x80004000, 52, 0, 0, 52, 32, 1, 0, 0, 0)
    struct.pack_into('>8I', elf, 52, 1, 128, 0x80004000, 0x80004000, 32, 64, 5, 32)
    return bytes(elf)


def run(peer, image):
    nonce = 0x12345678
    crc = zlib.crc32(image)
    hello = frame(1, nonce)
    ready = frame(2, nonce, (16*1024*1024-4096) | 0x80000000)
    def request(data, success=True):
        result = subprocess.run([str(peer.resolve())], input=data, capture_output=True, timeout=10)
        assert result.returncode == (0 if success else 1), result.stderr
        return result
    probe = request(hello+frame(8, nonce))
    assert probe.stdout == ready and b'VALIDATED' not in probe.stderr
    compressed = zlib.compress(image, level=9)
    if len(compressed) < len(image):
        upload_zlib = hello+frame(10, len(image), crc)+struct.pack('>I', len(compressed))+compressed
        compressed_good = request(upload_zlib+frame(5, nonce)+frame(9, nonce))
        assert compressed_good.stdout == ready+frame(4, len(image), crc)+frame(6, nonce)
        assert b'VALIDATED entry=' in compressed_good.stderr
        assert request(upload_zlib[:-1], False).stdout == ready
        corrupted = bytearray(compressed)
        corrupted[0] ^= 0xff
        assert request(hello+frame(10, len(image), crc)+struct.pack('>I', len(corrupted))+corrupted, False).stdout == ready+frame(7, 6)
        assert request(hello+frame(10, len(image), crc)+struct.pack('>I', 0), False).stdout == ready+frame(7, 6)
    upload = hello+frame(3, len(image), crc)+image
    good = request(upload+frame(5, nonce)+frame(9, nonce))
    assert good.stdout == ready+frame(4, len(image), crc)+frame(6, nonce)
    assert b'VALIDATED entry=' in good.stderr
    assert b'VALIDATED' not in request(upload+frame(5, nonce), False).stderr
    assert b'VALIDATED' not in request(upload+frame(5, nonce)+frame(8, nonce)).stderr
    assert request(upload+frame(8, nonce)).stdout == ready+frame(4, len(image), crc)
    assert request(upload+frame(5, nonce+1), False).stdout.endswith(frame(7, 1))
    assert request(hello+frame(3, len(image), crc ^ 1)+image, False).stdout == ready+frame(7, 3)
    assert request(hello+frame(3, 0xffffffff, 0), False).stdout == ready+frame(7, 2)
    assert request(hello+frame(3, len(image), crc)+image[:-1], False).stdout == ready
    print(f'Production C receiver: compressed and legacy upload, probe/cancel, {len(image)}-byte ELF, validated execute, rejection and truncation vectors passed.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--peer', type=Path, default=Path('build/stdio_receiver'))
    parser.add_argument('--elf', type=Path)
    args = parser.parse_args()
    run(args.peer, args.elf.read_bytes() if args.elf else fixture())
