# DolphinDotNet.Debuggers

Rider and Visual Studio integrations for DolphinDotNet, together with their shared
.NET DAP adapter, GDB/RSP client, protocol tests and acceptance probe.
The resident GameCube ELF receiver is built here and bundled with both plugins.

- [Rider setup and debugging](docs/rider-debugging.md)
- [Visual Studio setup and debugging](docs/visual-studio-debugging.md)
- [Upload and debug through Swiss and USB Gecko](docs/usb-gecko-launch.md)
- [BBA/USB ELF launch and receiver readiness](docs/console-launcher.md)
- [Build, test and release](docs/development.md)
- [SDK/compiler and native runtime](https://github.com/capz/DolphinDotNet)

Install the plugin ZIP or VSIX from this repository's [Releases](https://github.com/capz/DolphinDotNet.Debuggers/releases)
or successful Actions artifacts. Build your game using DolphinDotNet.Sdk; these
extensions attach using the resulting ELF and matching debug metadata, and can
optionally upload the ELF through Swiss/USB Gecko or our GameCube BBA/USB receiver
before attaching. The latter mode checks readiness and validates the transfer
before requesting execution. They do not
compile games or start a CLR on the console. Existing extension IDs and debugger
protocols are unchanged.

For BBA or USB Gecko uploads, choose **DolphinDotNet ELF launcher** and use
**Open bundled launcher folder** to find the bootable DOL. After normal application
exit the receiver returns to readiness for another upload. The same DOL and a
standalone launcher ZIP are included in debugger releases.

The source was extracted from `capz/DolphinDotNet` at
`4e87227492d8ea056e035dcc03ebd480fc5c59a4`, preserving the relevant Git history,
licenses and authorship. See [migration details](docs/development.md#migration).
