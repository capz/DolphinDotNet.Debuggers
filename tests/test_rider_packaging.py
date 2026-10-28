"""Reject startup hazards before distributing the installed plugin archive."""
import io
from pathlib import Path
import sys
import unittest
from zipfile import BadZipFile, ZipFile, ZIP_STORED

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'scripts'))
from verify_rider import verify_jars


def jar_bytes(entries):
    stream = io.BytesIO()
    with ZipFile(stream, 'w', ZIP_STORED) as archive:
        for name, data in entries.items():
            archive.writestr(name, data)
    return stream.getvalue()


class RiderPackagingTests(unittest.TestCase):
    def verify(self, entries):
        with ZipFile(io.BytesIO(jar_bytes(entries))) as archive:
            verify_jars(archive)

    def test_plugin_and_protocol_libraries_are_isolated(self):
        self.verify({'DolphinDotNet/lib/plugin.jar': jar_bytes({
            'com/dolphindotnet/rider/AttachAction.class': b'plugin',
            'org/eclipse/lsp4j/debug/StackFrame.class': b'protocol'})})

    def test_ide_classes_cannot_leak_into_plugin_libraries(self):
        for entry in ('com/intellij/openapi/project/Project.class',
                      'org/jetbrains/jps/service/impl/JpsServiceManagerImpl.class',
                      'META-INF/versions/25/com/intellij/openapi/project/Project.class'):
            with self.subTest(entry=entry), self.assertRaisesRegex(ValueError, 'Bundled IDE class'):
                self.verify({'DolphinDotNet/lib/library.jar': jar_bytes({entry: b'IDE'})})

    def test_missing_broken_and_corrupted_jars_are_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Missing Rider plugin JARs'):
            self.verify({})
        with self.assertRaises(BadZipFile):
            self.verify({'DolphinDotNet/lib/plugin.jar': b'broken'})
        # Keep the outer archive valid while damaging an inner JAR member's CRC.
        damaged = jar_bytes({'com/dolphindotnet/rider/AttachAction.class': b'original'})
        damaged = damaged.replace(b'original', b'corrupt!')
        with self.assertRaisesRegex(ValueError, 'JAR checksum failure'):
            self.verify({'DolphinDotNet/lib/plugin.jar': damaged})

    def test_corrupted_outer_zip_is_rejected(self):
        payload = jar_bytes({'DolphinDotNet/lib/plugin.jar': jar_bytes({'example.txt': b'original'})})
        payload = payload.replace(b'original', b'corrupt!')
        with ZipFile(io.BytesIO(payload)) as archive:
            with self.assertRaisesRegex(ValueError, 'ZIP checksum failure'):
                verify_jars(archive)


if __name__ == '__main__':
    unittest.main()
