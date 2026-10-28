"""Stage the verified console DOL for both IDE packages and release downloads."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

from verify_launcher_boot import check, check_dol

ROOT = Path(__file__).resolve().parents[1]
LAUNCHER = ROOT / 'tools/DolphinDotNet.Launcher'
IMAGE = 'ghcr.io/extremscorner/libogc2@sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85'
FILES = ('DolphinDotNet.Launcher.dol', 'README.md', 'LICENSE', 'build-info.json')


def stage(output, source=LAUNCHER / 'build'):
    source, output = Path(source), Path(output)
    dol = source / FILES[0]
    elf_image = (source / 'DolphinDotNet.Launcher.elf').read_bytes()
    dol_image = dol.read_bytes()
    check(elf_image, dol_image)
    commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    output.mkdir(parents=True, exist_ok=True)
    # Stage and hash the exact snapshot that passed validation, even if a build
    # replaces the source file while packaging is in progress.
    (output / dol.name).write_bytes(dol_image)
    shutil.copy2(LAUNCHER / 'README.md', output / 'README.md')
    shutil.copy2(ROOT / 'LICENSE', output / 'LICENSE')
    (output / 'build-info.json').write_text(json.dumps(dict(
        repository='capz/DolphinDotNet.Debuggers', commit=commit, toolchain=IMAGE,
        protocol='DNDL v1 with COMMIT', resident=True, uploadPort=4299,
        network='DHCP', usbGeckoSlots=[0, 1], hardwareTested=False,
        sha256=hashlib.sha256(dol_image).hexdigest()), indent=2) + '\n', encoding='utf-8')
    return output


def verify_bundle(read):
    """Verify bytes extracted from either IDE archive or the standalone ZIP."""
    payload = {name: read(name) for name in FILES}
    manifest = json.loads(payload['build-info.json'])
    if hashlib.sha256(payload[FILES[0]]).hexdigest() != manifest['sha256']:
        raise ValueError('Bundled launcher checksum mismatch')
    if not manifest.get('resident') or manifest.get('repository') != 'capz/DolphinDotNet.Debuggers':
        raise ValueError('Unexpected launcher build manifest')
    if not payload['README.md'] or not payload['LICENSE']:
        raise ValueError('Missing launcher instructions/license')
    check_dol(payload[FILES[0]])


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'out/launcher')
    parser.add_argument('--build', action='store_true', help='Build using Docker before staging')
    args = parser.parse_args()
    if args.build:
        subprocess.run(['docker', 'run', '--rm', '--mount', f'type=bind,source={ROOT},target=/src',
                        '-w', '/src/tools/DolphinDotNet.Launcher', IMAGE, 'make', '-j2'], check=True)
    print(stage(args.output))
