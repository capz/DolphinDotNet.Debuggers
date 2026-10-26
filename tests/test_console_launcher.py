"""DNDL v1 peer tests; no real console, drivers or executable payload is run."""
import hashlib
import json
import os
from pathlib import Path
import select
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import unittest
import zlib
from test_debug_adapter import ADAPTER, Target, SerialRelay, probe, write_symbol_fixture


def frame(kind, value=0, detail=0):
    return struct.pack('>4sHHII', b'DNDL', 1, kind, value, detail)


def read_exact(read, size):
    result = b''
    while len(result) < size:
        data = read(size-len(result))
        if not data:
            raise EOFError('Incomplete launcher frame')
        result += data
    return result


def elf_fixture():
    elf = bytearray(256)
    elf[:7] = b'\x7fELF\x01\x02\x01'
    struct.pack_into('>HHIIIIIHHHHHH', elf, 16, 2, 20, 1, 0x80004000, 52, 0, 0, 52, 32, 1, 0, 0, 0)
    struct.pack_into('>8I', elf, 52, 1, 128, 0x80004000, 0x80004000, 32, 64, 5, 32)
    elf[128:160] = bytes(range(32))
    return bytes(elf)


def exchange(read, write, expected, *, reject=False, wrong_nonce=False, native_command=None):
    if native_command:
        # Optional shared native receiver fixture (stdio_receiver.c), including a
        # Docker command. It validates ELF bytes but never executes them.
        process = subprocess.Popen(native_command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        def native_write(data):
            process.stdin.write(data)
            process.stdin.flush()
        try:
            native_write(read_exact(read, 16))
            write(read_exact(process.stdout.read, 16))
            request = read_exact(read, 16)
            native_write(request)
            _, _, kind, size, _ = struct.unpack('>4sHHII', request)
            if kind == 8:
                process.stdin.close()
                assert process.wait(timeout=5) == 0
                return False
            image = read_exact(read, size)
            assert image == expected
            native_write(image)
            accepted = read_exact(process.stdout.read, 16)
            assert struct.unpack('>4sHHII', accepted)[2] == 4, accepted
            write(accepted)
            native_write(read_exact(read, 16))
            write(read_exact(process.stdout.read, 16))
            native_write(read_exact(read, 16))  # COMMIT after EXECUTING reached the host.
            process.stdin.close()
            assert process.wait(timeout=5) == 0, process.stderr.read()
            assert b'VALIDATED entry=80004000 segments=1' in process.stderr.read()
            return True
        finally:
            if process.poll() is None:
                process.kill()
            process.wait(timeout=5)
            process.stdout.close()
            process.stderr.close()
    hello = read_exact(read, 16)
    magic, version, kind, nonce, zero = struct.unpack('>4sHHII', hello)
    assert (magic, version, kind, zero) == (b'DNDL', 1, 1, 0), hello
    write(frame(2, nonce ^ int(wrong_nonce), 16*1024*1024-4096))
    if wrong_nonce:
        assert read(1) == b''
        return False
    request = read_exact(read, 16)
    _, _, kind, value, detail = struct.unpack('>4sHHII', request)
    if kind == 8:
        assert (value, detail) == (nonce, 0)
        return False
    assert kind == 3
    image = read_exact(read, value)
    assert image == expected
    assert detail == zlib.crc32(image)
    if reject:
        write(frame(7, 4))
        assert read(1) == b''
        return False
    write(frame(4, value, detail))
    assert read_exact(read, 16) == frame(5, nonce)
    write(frame(6, nonce))
    assert read_exact(read, 16) == frame(9, nonce)
    return True


class Receiver:
    def __init__(self, image, **options):
        self.image, self.options = image, options
        self.listener = socket.socket()
        self.listener.bind(('127.0.0.1', 0))
        self.port = self.listener.getsockname()[1]
        self.listener.listen()
        self.listener.settimeout(.1)
        self.stop = threading.Event()
        self.completed = threading.Event()
        self.errors = []
        self.executed = []
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def run(self):
        while not self.stop.is_set():
            try:
                peer, _ = self.listener.accept()
            except socket.timeout:
                continue
            except OSError:
                return
            try:
                with peer:
                    peer.settimeout(10)
                    self.executed.append(exchange(peer.recv, peer.sendall, self.image, **self.options))
            except Exception as error:
                self.errors.append(error)
            finally:
                self.completed.set()

    def close(self):
        self.stop.set()
        self.listener.close()
        self.thread.join(timeout=3)


class NativeSerialRelay(SerialRelay):
    def __init__(self, port, image):
        self.image = image
        super().__init__(port)

    def run(self):
        def read(size):
            ready, _, _ = select.select([self.master], [], [], 5)
            if not ready:
                raise TimeoutError('Missing serial launcher data')
            return os.read(self.master, size)
        try:
            assert exchange(read, lambda data: os.write(self.master, data), self.image)
            self.upload_received.set()
            super().run()
        except Exception as error:
            if not self.stopping.is_set():
                self.errors.append(error)


class ConsoleLauncherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.elf, self.source, self.address = write_symbol_fixture(Path(self.temp.name))
        self.elf.write_bytes(elf_fixture())
        data = json.loads(self.address.read_text())
        data['elfSha256'] = hashlib.sha256(self.elf.read_bytes()).hexdigest()
        self.address.write_text(json.dumps(data))
        self.client = probe.DapProcess(ADAPTER)
        self.client.request('initialize', dict(pathFormat='path', linesStartAt1=True))
        self.receiver = self.target = self.serial = None

    def tearDown(self):
        self.client.close()
        if self.receiver:
            self.receiver.close()
            self.assertFalse(self.receiver.errors, self.receiver.errors)
        if self.serial:
            self.serial.close()
            self.assertFalse(self.serial.errors, self.serial.errors)
        if self.target:
            self.target.thread.join(timeout=3)
            self.assertFalse(self.target.errors, self.target.errors)
        self.temp.cleanup()

    def arguments(self, **options):
        return dict(transport='tcp', host='127.0.0.1', port=2166, launcher='dolphinDotNet',
                    readyTimeoutSeconds=2, uploadTimeoutSeconds=3, bootDelayMilliseconds=0,
                    elf=str(self.elf), sourceMap=str(self.source), addressMap=str(self.address)) | options

    def complete_debug_session(self):
        self.client.event('initialized')
        output = [e.get('body', {}).get('output', '') for e in self.client.events]
        self.assertTrue(any('READY TO RECEIVE ELF' in s for s in output))
        self.assertTrue(any('acknowledged execution' in s for s in output))
        self.client.request('setBreakpoints', dict(source=dict(path='/build/日本/Game.cs'), breakpoints=[dict(line=11)]))
        self.client.request('configurationDone')
        self.client.event('stopped')
        self.client.request('continue', dict(threadId=1))
        self.assertEqual('breakpoint', self.client.event('stopped')['reason'])
        self.client.request('disconnect')
        self.client.event('terminated')

    def tcp_launch(self, native_command=None):
        self.receiver = Receiver(self.elf.read_bytes(), native_command=native_command)
        self.target = Target()
        self.client.request('launch', self.arguments(launcherPort=self.receiver.port, port=self.target.port))
        self.complete_debug_session()
        self.assertTrue(self.receiver.completed.wait(5))
        self.assertEqual([True], self.receiver.executed)

    def test_bba_ready_upload_validated_execution_then_gdb(self):
        self.tcp_launch()

    @unittest.skipUnless(os.environ.get('DND_NATIVE_LAUNCHER_COMMAND'), 'Optional production C receiver integration')
    def test_production_c_receiver_then_gdb(self):
        self.tcp_launch(json.loads(os.environ['DND_NATIVE_LAUNCHER_COMMAND']))

    def test_configuration_readiness_probe_never_uploads_and_releases_receiver(self):
        self.receiver = Receiver(self.elf.read_bytes())
        settings = dict(transport='tcp', host='127.0.0.1', port=2166, launcherPort=self.receiver.port, readyTimeoutSeconds=2)
        for stdin_mode in (False, True):
            command = ['dotnet', str(ADAPTER), '--check-launcher-stdin'] if stdin_mode else ['dotnet', str(ADAPTER), '--check-launcher', json.dumps(settings)]
            result = subprocess.run(command, input=json.dumps(settings) if stdin_mode else None, capture_output=True, text=True, timeout=10)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertTrue(json.loads(result.stdout)['ready'])
        self.receiver.completed.wait(3)
        self.assertEqual([False, False], self.receiver.executed)

    def test_receiver_rejects_elf_without_execution_or_gdb(self):
        self.receiver = Receiver(self.elf.read_bytes(), reject=True)
        result = self.client.request('launch', self.arguments(launcherPort=self.receiver.port), success=False)
        self.assertIn('unsupported/malformed ELF', result['message'])
        self.assertFalse(any(e.get('event') == 'initialized' for e in self.client.events))

    def test_wrong_nonce_never_uploads(self):
        self.receiver = Receiver(self.elf.read_bytes(), wrong_nonce=True)
        result = self.client.request('launch', self.arguments(launcherPort=self.receiver.port), success=False)
        self.assertIn('stale launcher readiness', result['message'])

    def test_not_ready_is_bounded_and_reports_readiness_failure(self):
        with socket.socket() as endpoint:
            endpoint.bind(('127.0.0.1', 0))
            port = endpoint.getsockname()[1]  # Bound but not listening.
            result = self.client.request('launch', self.arguments(launcherPort=port, readyTimeoutSeconds=1), success=False)
            self.assertIn('not ready', result['message'])

    def test_invalid_receiver_settings_precede_connection(self):
        for options, expected in [({'readyTimeoutSeconds': 0}, 'readyTimeoutSeconds'),
                ({'uploadTimeoutSeconds': 61}, 'uploadTimeoutSeconds'), ({'launcherPort': 0}, 'upload port'),
                ({'launcherPort': 2166}, 'differ'), ({'programArguments': ['unsupported']}, 'programArguments'),
                ({'launcher': 'unknown'}, 'launcher must')]:
            with self.subTest(options=options):
                result = self.client.request('launch', self.arguments(**options), success=False)
                self.assertIn(expected, result['message'])

    @unittest.skipUnless(sys.platform.startswith('linux'), 'Linux serial PTY launch handoff')
    def test_usb_ready_upload_execution_and_rsp_on_same_device(self):
        self.target = Target()
        self.serial = NativeSerialRelay(self.target.port, self.elf.read_bytes())
        self.client.request('launch', self.arguments(transport='usbGecko', serialPort=self.serial.path, baudRate=115200))
        self.assertTrue(self.serial.upload_received.is_set())
        self.complete_debug_session()


if __name__ == '__main__':
    unittest.main()
