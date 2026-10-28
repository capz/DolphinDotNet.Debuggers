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

Version 0.3.1 includes host upload progress and libogc2 bulk-register compatibility.
Copy the newly bundled launcher DOL to the console for the USB batching fix,
footer progress bar and logo; updating the plugin alone does not update a running DOL.

C# gutter breakpoints recognize DolphinDotNet SDK projects without selecting an
attach configuration. Existing attach configurations remain available for
multilaunch workflows.

The plugin provides saved connection targets and an XDebugger → stdio DAP bridge
with read-only primitive Arguments/Locals. Requires Rider 2026.2 (pinned build SDK 2026.2.3.1),
Java 25, .NET 8 and Python 3 for builds/tests.

```sh
./gradlew test buildPlugin verifyPlugin
```

On Windows, set `PYTHON` to a Python executable if `python3` is unavailable.

Before release, install the exact distribution ZIP into an isolated Rider profile,
open a project, exit Rider, and start the same profile again. API compatibility
verification alone does not exercise IDE startup. Keep the test profile's logs.
The package verifier also checks ZIP/JAR checksums and rejects bundled IntelliJ
and JPS implementation classes, which must come from Rider itself.

If Rider fails to start after installation, preserve `idea.log` and disable this
plugin by moving only `plugins/DolphinDotNet.Rider` outside the Rider configuration
directory while Rider is closed. Keep the rest of your settings and caches.
See JetBrains' [directory reference](https://www.jetbrains.com/help/rider/Directories_Used_by_the_IDE_to_Store_Settings_Caches_Plugins_and_Logs.html).
A `ClassFormatError` naming a built-in JetBrains class needs investigation of
that class and the IDE's state; it does not identify the plugin as the cause.
The [startup investigation](../../docs/rider-startup-investigation.md) records
the reported failure and the limits of the isolated reproduction checks.

The ZIP in `build/distributions` includes the adapter. Requires the .NET 8 runtime
on the Rider process PATH. This is an initial integration implementation;
interactive device debugging and physical hardware acceptance remain pending. Debugger targets
must be Debug builds; Release target debugging is out of scope.

See [configuration, target properties, protocol and acceptance](../../docs/rider-debugging.md).

Build only this plugin in this repository's GitHub Actions using **Rider plugin →
Run workflow**. The debugger CI workflow packages both integrations, and
successful main builds attach the installable ZIP/VSIX to this repository's
releases. See [development and releases](../../docs/development.md).
