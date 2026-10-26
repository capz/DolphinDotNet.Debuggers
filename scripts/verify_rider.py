"""Verify the installed plugin ZIP's adapter and bootable console launcher."""
import sys
from zipfile import ZipFile
from package_launcher import verify_bundle


def verify(path):
    with ZipFile(path) as archive:
        names = archive.namelist()
        for required in ('DolphinDotNet.DebugAdapter.dll', 'DolphinDotNet.DebugAdapter.deps.json',
                         'DolphinDotNet.DebugAdapter.runtimeconfig.json', 'DolphinDotNet.Debugger.dll', 'System.IO.Ports.dll'):
            if not any(name.endswith('/adapter/' + required) for name in names):
                raise ValueError('Missing adapter file: ' + required)
        if not any('Ports.Native' in name for name in names):
            raise ValueError('Missing serial native runtimes')
        manifests = [name for name in names if name.endswith('/launcher/build-info.json')]
        if len(manifests) != 1:
            raise ValueError('Expected one bundled launcher')
        prefix = manifests[0][:-len('build-info.json')]
        verify_bundle(lambda name: archive.read(prefix + name))
    print('Rider adapter, serial dependencies and resident GameCube DOL verified')


if __name__ == '__main__':
    verify(sys.argv[1])
