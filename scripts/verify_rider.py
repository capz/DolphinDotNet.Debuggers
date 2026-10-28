"""Verify the installed plugin ZIP's adapter and bootable console launcher."""
import sys
import io
from zipfile import ZipFile
from package_launcher import verify_bundle


def verify_jars(archive):
    """Fail packaging on damaged JARs or accidentally bundled IDE classes."""
    damaged = archive.testzip()
    if damaged:
        raise ValueError('Plugin ZIP checksum failure: ' + damaged)
    jars = [name for name in archive.namelist() if name.endswith('.jar')]
    if not jars:
        raise ValueError('Missing Rider plugin JARs')
    for name in jars:
        with ZipFile(io.BytesIO(archive.read(name))) as jar:
            damaged = jar.testzip()
            if damaged:
                raise ValueError('Plugin JAR checksum failure: ' + name + ': ' + damaged)
            for entry in jar.namelist():
                if entry.endswith('.class') and entry.startswith(('com/intellij/', 'org/jetbrains/jps/')):
                    raise ValueError('Bundled IDE class: ' + name + ': ' + entry)


def verify(path):
    with ZipFile(path) as archive:
        verify_jars(archive)
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
    print('Rider JAR integrity/isolation, adapter, serial dependencies and resident GameCube DOL verified')


if __name__ == '__main__':
    verify(sys.argv[1])
