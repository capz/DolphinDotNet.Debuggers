# DolphinDotNet Rider plugin

**Run → Attach to Nintendo GameCube…**, immediately below **Attach to Remote Process…**,
opens the attach dialog directly. Choose the
Debug build folder and a saved BBA/TCP or USB Gecko target, then click **Attach**.
The dialog remembers its settings per project on this machine and reuses existing
attach configuration defaults on first use when selected or unambiguous. Launch Dolphin/the device first.
For a USB Gecko target, **Upload with Swiss (USB Gecko)** can instead send
the selected Debug ELF to a console waiting in Swiss. See [USB Gecko launching](../../docs/usb-gecko-launch.md).
**DolphinDotNet ELF launcher (BBA / USB Gecko)** adds receiver-readiness checks
and acknowledged ELF execution through our console loader. See [setup](../../docs/console-launcher.md).

C# gutter breakpoints recognize DolphinDotNet SDK projects without selecting an
attach configuration. Existing attach configurations remain available for
multilaunch workflows.

The plugin provides saved connection targets and an XDebugger → stdio DAP bridge
with read-only primitive Arguments/Locals. Requires Rider 2026.2 (pinned build SDK 2026.2.3.1),
Java 25, .NET 8 and Python 3 for builds/tests.

```sh
./gradlew test buildPlugin verifyPlugin
```

The ZIP in `build/distributions` includes the adapter. Requires the .NET 8 runtime
on the Rider process PATH. This is an initial integration implementation;
interactive Rider and physical hardware acceptance remain pending. Debugger targets
must be Debug builds; Release target debugging is out of scope.

See [configuration, target properties, protocol and acceptance](../../docs/rider-debugging.md).

Build only this plugin in this repository's GitHub Actions using **Rider plugin →
Run workflow**. The debugger CI workflow packages both integrations, and
successful main builds attach the installable ZIP/VSIX to this repository's
releases. See [development and releases](../../docs/development.md).
