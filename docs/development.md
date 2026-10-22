# Building and releasing the debugger integrations

This repository builds independently. Install .NET 8 and Python 3; Rider builds
also need Java 25 and use the checked-in Gradle 9.1 wrapper. Visual Studio VSIX
packaging needs Windows with Visual Studio/MSBuild and the VSSDK build tools.

```sh
dotnet run --project tests/DebuggerTests -c Release
dotnet build tools/DolphinDotNet.DebugAdapter -c Release
python3 -m unittest discover -s tests -p test_debug_adapter.py
dotnet run --project tests/VisualStudioTests -c Release
cd plugins/rider
./gradlew test buildPlugin verifyPlugin
```

On Windows use `gradlew.bat` and `python` as appropriate. Build the VSIX from a
Visual Studio Developer Command Prompt:

```bat
msbuild plugins/visual-studio/DolphinDotNet.VisualStudio.csproj /restore /p:Configuration=Release /p:DeployExtension=false
```

The Rider ZIP bundles a framework-dependent adapter; the VSIX bundles a
self-contained Windows adapter. Adapter source is local under `tools/`; neither
build needs a sibling SDK checkout or a published SDK package. The compiler
produces the ELF/source/address metadata; the adapter validates their identities.

## CI and releases

The main workflow runs DAP/RSP tests on Windows and macOS, builds the Rider plugin
on macOS with JetBrains Plugin Verifier, and builds/verifies the VSIX on Windows.
Each packaging workflow can also be manually dispatched. PR runs retain Actions
artifacts. Successful main builds publish a prerelease containing the ZIP and
VSIX produced by that same run. Marketplace publication and signing remain
separate release steps; do not publish to a marketplace merely to test packaging.

Interactive IDE sessions and physical BBA/USB Gecko hardware require acceptance
on those hosts. The serial PTY fixture runs only on Linux; Windows/macOS CI does
not establish Linux PTY behavior. Local Linux checks remain useful.

## SDK boundary tests

The standalone adapter suite does not need compiler sources. Two integration
checks exercise compiler-generated variable ranges and the native heartbeat.
They run in the SDK repository's compatibility job with `DND_RUNTIME_ROOT` set
to its checkout, using a pinned checkout of this repository. For a deliberate
local combined run, set `DND_RUNTIME_ROOT=/absolute/path/to/DolphinDotNet` before
running the Python adapter tests. Without it those two boundary tests are
explicitly skipped. Host C heartbeat execution additionally requires Linux/cc.

The SDK's `eng/debugger-source.json` pins a reviewed debugger commit. Its pack
script fetches that revision into `out/debuggers`, publishes the adapter into
`DolphinDotNet.Sdk`, and retains the standalone DAP probe beside the adapter.
Changing the pin requires SDK consumer/compatibility validation; moving code does
not imply all future debugger versions are compatible.

## Migration

The initial `main` branch contains the filtered original history for the moved
paths. The standalone-build PR adds repository-local documentation, CI and
consumer project fixtures. Merge that PR before the SDK removal PR. The SDK
removal PR pins the exact validated debugger commit, so the sources remain
retrievable even before it is merged here. No history in the SDK repository was
rewritten. Compiler metadata generation and native debugging support remain in
the SDK repository; all IDE/DAP/RSP implementation lives here.
