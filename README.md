# DolphinDotNet.Debuggers

Rider and Visual Studio integrations for DolphinDotNet, together with their shared
.NET DAP adapter, GDB/RSP client, protocol tests and acceptance probe.

- [Rider setup and debugging](docs/rider-debugging.md)
- [Visual Studio setup and debugging](docs/visual-studio-debugging.md)
- [Build, test and release](docs/development.md)
- [SDK/compiler and native runtime](https://github.com/capz/DolphinDotNet)

Install the plugin ZIP or VSIX from this repository's [Releases](https://github.com/capz/DolphinDotNet.Debuggers/releases)
or successful Actions artifacts. Build your game using DolphinDotNet.Sdk; these
extensions attach to the resulting ELF and matching debug metadata. They do not
compile games or start a CLR on the console. Existing extension IDs and debugger
protocols are unchanged.

The source was extracted from `capz/DolphinDotNet` at
`4e87227492d8ea056e035dcc03ebd480fc5c59a4`, preserving the relevant Git history,
licenses and authorship. See [migration details](docs/development.md#migration).
