# Upload a Debug build through Swiss and USB Gecko

Rider and Visual Studio can upload the selected build's ELF through USB Gecko,
then attach to its GDB stub. The adapter implements wiiload 0.5 itself using
.NET's existing serial and zlib libraries. No wiiload executable, libftdi,
external compression tool, or additional package is required.

## Prepare the console

1. Build the game in Debug with `DndGdbTransport=UsbGecko` and
   `DndGdbChannel=0` (slot A) or `1` (slot B). The native GDB stub must wait at
   startup for the debugger. Keep the ELF and its matching source/address maps
   together in the selected Debug build folder.
2. Boot Swiss on the console. Enable its USB Gecko wiiload receiver for the
   connected slot. Swiss must be running and ready to receive; an arbitrary
   game or a running GDB stub does not understand wiiload uploads.
3. Install the USB Gecko virtual serial port driver, identify its COM/device
   path, and close terminal programs or other debuggers using that port.

The host cannot select the console's EXI slot. Swiss and the uploaded build must
use the connected slot. The existing baud setting is a VCP driver compatibility
value, not a claim about USB FIFO speed.

## IDE settings

In Rider's **Attach to DolphinDotNet** dialog or its saved attach configuration,
select a **USB Gecko** target and choose **Upload with Swiss (USB Gecko)** under
**Start on device**. The same mode is available in Visual Studio's attach dialog.
Existing configurations retain their mode; Swiss upload requires a USB target.
Settings persist with the existing local preferences/run configuration.

For BBA or an explicit receiver-readiness handshake, choose the separate
[DolphinDotNet ELF launcher mode](console-launcher.md).

The ELF discovered in the Debug build folder is the uploaded executable.
Swiss supports ELF loading; using this file directly ties the uploaded bytes to
the identity already checked against the debugger's maps. A separate DOL and a
manual program path are unnecessary. This launches an existing build; it does
not build the project.

**Upload timeout** defaults to 60 seconds (range 1–120) and bounds transfer and
driver drain. **Boot delay** defaults to 1000 milliseconds (range 0–30000),
allowing Swiss to start the application before opening the port for GDB.
Increase it if the GDB query reaches the console before its stub is ready.
The target's existing **Timeout** applies separately to GDB connection/requests.

The debug console reports upload and attach phases. Successful upload means
that bytes were sent; wiiload has no receiver acknowledgement. Only a successful
GDB attach proves that the debug stub is responding. Once attached, set source
breakpoints and Resume as usual. Disconnect releases the debugger without
resetting the console. To upload again, return to Swiss first.

## DAP contract

Send `initialize`, then `launch` with these arguments (absolute paths shown):

```json
{
  "transport": "usbGecko",
  "serialPort": "COM5",
  "baudRate": 115200,
  "timeoutSeconds": 10,
  "uploadTimeoutSeconds": 60,
  "bootDelayMilliseconds": 1000,
  "elf": "/build/Game.elf",
  "sourceMap": "/build/Game.dnd-debug.json",
  "addressMap": "/build/Game.dnd-addresses.json",
  "programArguments": ["--name", "two words"],
  "sourceFileMap": {"/build/src": "/local/src"}
}
```

`programArguments` is an optional array of strings for direct DAP clients. The
ELF basename is always argv[0]. The combined UTF-8 argument block, including NUL
terminators and its final empty string, is limited to 1024 bytes. Embedded NULs
are rejected. The image must be nonempty and at most 32 MiB; actual available
console memory may be smaller. `noDebug=true` and TCP launch are unsupported.

All settings, maps and the upload snapshot's ELF hash are checked before opening
the port. A failed upload never proceeds to GDB. The serial port is disposed on
failure and after a successful drain, then reopened using the existing RSP
transport. The adapter sends `initialized` only after GDB responds; breakpoint
configuration and `configurationDone` follow the existing attach lifecycle.
Stopping the IDE session during startup closes the adapter after its existing
disconnect grace period, releasing OS handles. An interrupted upload can leave
Swiss waiting for the rest of a frame: reboot/return to Swiss before retrying.

## Protocol and validation

The frame is a 16-byte header (`HAXX`, big-endian version `0x0005`, 16-bit
argument length, 32-bit compressed length, 32-bit original length), followed by
a zlib stream and the argument block. Compression is always used, even when it
increases size, because Swiss's receiver always inflates. Serial writes use
blocks of at most 3968 bytes and RTS/CTS for the wiiload phase. GDB uses the
existing serial settings after the upload port has closed. No upload ACK or
extra serial command is invented.

Protocol references: [Swiss receiver](https://github.com/emukidid/swiss-gc/blob/master/cube/swiss/source/wiiload.c),
[wiiload framing](https://github.com/devkitPro/wiiload/blob/master/source/main.c),
and [wiiload serial transport](https://github.com/devkitPro/wiiload/blob/master/source/gecko.c).
The implementation is original C# and introduces no new dependency.

`tests/DebuggerTests` checks framing, endianness, compression (including
incompressible input), UTF-8 arguments, byte limits, cancellation and failed
writes. Adapter tests reject invalid/stale launch requests before hardware
access. Linux CI also receives an entire upload over a PTY, independently
inflates it with Python zlib, and then runs a GDB breakpoint/disconnect session
on that same device. IDE tests check opt-in launch arguments and persistence.
These checks do not establish physical FTDI/EXI compatibility. Acceptance on
Windows/Linux/macOS with Swiss and a USB Gecko remains a hardware check: verify
upload, GDB startup, breakpoint, Resume, disconnect, and a second upload after
returning to Swiss.

If upload fails, check exclusive port access, driver/OS permissions, Swiss's
receiver and slot, and cable/device identity. If upload completes but attach
fails, check the USB Gecko GDB build settings and boot delay, and restart Swiss
before retrying. Do not point the uploader at a serial device unrelated to the
console.
