package com.dolphindotnet.rider;

import com.intellij.openapi.application.PathManager;
import com.intellij.execution.configurations.GeneralCommandLine;
import java.nio.file.*;

final class AdapterCommand {
    static GeneralCommandLine create(String... arguments) {
        // The distribution places this JAR in <plugin>/lib and the adapter in
        // <plugin>/adapter. Use the public class-location API, not the internal
        // plugin registry, so this also follows custom IDE plugin directories.
        Path adapter = pluginDirectory().resolve("adapter/DolphinDotNet.DebugAdapter.dll");
        if (!Files.isRegularFile(adapter)) throw new IllegalStateException("The plugin package is missing its debug adapter.");
        return new GeneralCommandLine("dotnet", adapter.toString()).withParameters(arguments);
    }
    private static Path pluginDirectory() {
        var jar = PathManager.getJarForClass(AdapterCommand.class);
        if (jar == null || !Files.isRegularFile(jar) || jar.getParent() == null || jar.getParent().getParent() == null)
            throw new IllegalStateException("DolphinDotNet plugin location is unavailable.");
        return jar.getParent().getParent();
    }
    static Path launcherDirectory() {
        var directory = pluginDirectory().resolve("launcher");
        if (!Files.isRegularFile(directory.resolve("DolphinDotNet.Launcher.dol")))
            throw new IllegalStateException("The plugin package is missing its console launcher.");
        return directory;
    }
}
