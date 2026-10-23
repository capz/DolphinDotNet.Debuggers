# Launch through DolphinDotNet's GameCube ELF receiver

The native [DolphinDotNet.Launcher](https://github.com/capz/DolphinDotNet/tree/feature/gamecube-executable-receiver/tools/DolphinDotNet.Launcher)
boots on a GameCube and receives an ELF through BBA/TCP or USB Gecko. Its DNDL v1
protocol exchanges readiness and validation acknowledgements; it is independent
of the existing Swiss/wiiload mode. No external uploader or new host dependency
is used.

## Configure and check readiness

1. Build and boot `DolphinDotNet.Launcher.dol` using the companion SDK repository's
   instructions. Its screen shows BBA DHCP/static configuration and the upload
   endpoint. USB Gecko is available on slots A/B while BBA configures.
2. Build the application in Debug with a waiting GDB stub matching the target:
   `DndGdbTransport=Tcp` with the console's IP/netmask/gateway and GDB port, or
   `DndGdbTransport=UsbGecko` with `DndGdbChannel=0` (A) or `1` (B).
3. In Rider's attach dialog/saved run configuration, or Visual Studio's attach
   dialog, choose **DolphinDotNet ELF launcher (BBA / USB Gecko)** under **Start
   on device**. Select the matching Debug build folder and target.
4. For TCP, set **Launcher TCP upload port** to the receiver's port (default
   **4299**). The target's **TCP port** remains the application's **GDB** port,
   normally 2166 or 2828. These must differ. For USB use its serial device and
   existing driver baud compatibility value; the port picker setting is not used.
5. Click **Check receiver readiness**. The plugin runs a background handshake
   and displays ready/not-ready status. The console indicates that the IDE is
   connected and ready to receive. The check then cancels its session without
   uploading or executing anything, freeing the port for a later launch.

Readiness is a snapshot of the selected endpoint. Changing connection settings
invalidates it; saving a configuration does not preserve a live connection.
Every launch performs a fresh two-way readiness handshake, waits for the
receiver if needed, reports **READY TO RECEIVE ELF** in the debug console,
uploads the exact validated ELF snapshot, verifies its acceptance, requests
execution, then closes the upload connection and attaches to GDB. The console
shows the corresponding receive/validate/execute states.

**Readiness wait** defaults to 30 seconds (1–120). The manual check waits at most
3 seconds; launch uses the full configured wait. **Upload timeout** defaults to
60 seconds (1–60 for our receiver, whose whole native session also has a
60-second deadline). **Boot delay** defaults to 1000 milliseconds (0–30000),
then the existing target response timeout applies to GDB. Rider reserves enough
startup time for these phases. Cancelling the IDE session reaps the adapter
after its disconnect grace period; the OS releases its ports.

The receiver is a one-shot chainloader and is replaced by the application. Boot
it again before the next upload. For TCP an abandoned connection aborts the
session. USB cannot signal host EOF; after an interrupted partial upload, wait
for the receiver's deadline or restart it before retrying. Uploads and execution
requests are never automatically replayed.

## DAP and readiness command

Use `launch` with these arguments after `initialize`:

```json
{
  "launcher": "dolphinDotNet",
  "transport": "tcp",
  "host": "192.168.1.80",
  "port": 2828,
  "launcherPort": 4299,
  "readyTimeoutSeconds": 30,
  "uploadTimeoutSeconds": 60,
  "bootDelayMilliseconds": 1000,
  "timeoutSeconds": 10,
  "elf": "/build/Game.elf",
  "sourceMap": "/build/Game.dnd-debug.json",
  "addressMap": "/build/Game.dnd-addresses.json"
}
```

For USB replace transport/host/port with `"transport": "usbGecko"`,
`"serialPort": "COM5"`, `"baudRate": 115200`. The same serial port is used for
readiness/upload, released, then reopened for GDB. Setting `launcher` to `swiss`
(or omitting it on `launch`) preserves the earlier USB-only wiiload behavior.
Ordinary `attach` never uploads or performs a receiver handshake.

The adapter's `--check-launcher <JSON>` mode prints one JSON object:
`{"ready":true,"maximumImageBytes":16773120,"message":"..."}` on success,
or `{"ready":false,"message":"..."}` with exit code 1 on failure. It takes the
connection/launcher/readiness fields above and requires no artifact paths.
`--check-launcher-stdin` accepts the same JSON on stdin for clients that want to
avoid shell argument quoting. Neither command uses DAP framing or sends an ELF.

## Limits and diagnostics

The console accepts fixed-address big-endian PowerPC ELF32 ET_EXEC in cached
MEM1, with up to 16 nonoverlapping load segments and no dynamic linker. The
advertised file limit is **16 MiB minus 4096 bytes**. The entire file and a small
handoff block must fit in a staging range disjoint from its load destinations;
an ELF may therefore be rejected despite fitting the file-size limit. BSS is
zeroed. Program arguments and `noDebug=true` are unsupported in this minimal
mode. The host checks ELF/debug-map identity before contacting hardware; the
receiver independently checks checksum, headers and memory ranges before
acceptance. The ELF's native CRT initializes the new application runtime.

Readiness proves that this receiver responded to the current nonce. Acceptance
proves that its ELF checks passed. Execution acknowledgement proves that a
handoff was requested; the subsequent GDB attach proves that the application's
debugger is responding. A GDB failure after execution does not cause a second
upload. Read the console/IDE status and return to the receiver before retrying.

If not ready, check that our launcher is booted, the console address/upload port
or serial device is correct, and no other process owns the serial port. If an
ELF is rejected, check its architecture, size, native load ranges and staging
collision. If GDB does not connect, check the application's build-time debugger
settings, network configuration/EXI slot and boot delay. The launcher does not
configure the uploaded application's network or change its GDB transport.

DNDL uses 16-byte `DNDL`/version-1 frames, big-endian fields, a per-session nonce,
uncompressed ELF bytes and IEEE CRC32. See the native launcher README for the
complete wire contract. CRC32/nonce are not authentication; use a trusted LAN.
After the execution acknowledgement the host sends a final COMMIT, proving that
the last reply arrived before the console shuts down networking. The receiver
does not execute without that confirmation.

The suites cover host protocol limits and stale/wrong replies; readiness-only
probe/cancel; BBA upload, execution acknowledgement and GDB breakpoints; Linux
USB PTY upload-to-RSP handoff; and IDE configuration persistence. An optional
`DND_NATIVE_LAUNCHER_COMMAND` JSON command array lets the .NET adapter test use
the production C receiver's `stdio_receiver` fixture, which validates bytes and
never executes them. Native compilation and this cross-repository test have
been run locally. Physical GameCube/BBA/Gecko and interactive IDE acceptance
remain separate checks.
