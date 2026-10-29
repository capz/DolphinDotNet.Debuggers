package com.dolphindotnet.rider;

import com.intellij.execution.*;
import com.intellij.execution.configurations.*;
import com.intellij.execution.executors.*;
import com.intellij.execution.filters.TextConsoleBuilderFactory;
import com.intellij.execution.process.*;
import com.intellij.execution.runners.*;
import com.intellij.execution.ui.*;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.progress.*;
import com.intellij.openapi.util.Disposer;
import com.intellij.xdebugger.*;
import org.jetbrains.annotations.NotNull;
import org.jetbrains.concurrency.*;
import java.net.ServerSocket;
import java.nio.file.*;
import java.util.*;

public final class LaunchRunner extends AsyncProgramRunner<RunnerSettings> {
    @Override public @NotNull String getRunnerId() { return "DolphinDotNetLaunchRunner"; }
    @Override public boolean canRun(@NotNull String executor, @NotNull RunProfile profile) {
        if (!(profile instanceof LaunchConfiguration c)) return false;
        return c.mode.equals("play") ? executor.equals(DefaultRunExecutor.EXECUTOR_ID) : executor.equals(DefaultDebugExecutor.EXECUTOR_ID);
    }
    @Override protected @NotNull Promise<RunContentDescriptor> execute(@NotNull ExecutionEnvironment environment, @NotNull RunProfileState state) {
        var result = new AsyncPromise<RunContentDescriptor>();
        var configuration = (LaunchConfiguration)environment.getRunProfile();
        // Snapshot settings before leaving the UI thread; edits cannot change a build mid-flight.
        var mode = configuration.mode; var file = configuration.projectFile(); var dolphin = configuration.dolphin; var name = configuration.getName();
        var target = mode.equals("target") ? LaunchConfiguration.firstTarget() : null;
        ProgressManager.getInstance().run(new Task.Backgroundable(environment.getProject(), "Building " + file.getFileName(), true) {
            @Override public void run(@NotNull ProgressIndicator indicator) {
                TargetConnectionGate.Lease lease = null;
                Process emulator = null;
                try {
                    if (!mode.equals("play")) lease = TargetConnectionGate.acquire(indicator::checkCanceled);
                    runChecked(new GeneralCommandLine("dotnet").withParameters(LaunchPlan.build(file, mode, target)).withWorkDirectory(file.getParent().toFile()), indicator);
                    var folder = Path.of(runChecked(new GeneralCommandLine("dotnet", "msbuild", file.toString(), "-getProperty:TargetDir", "-property:Configuration=Debug").withWorkDirectory(file.getParent().toFile()), indicator).trim());
                    var args = new LinkedHashMap<String, Object>();
                    if (!mode.equals("play")) args.putAll(BuildArtifacts.discover(folder).attachArguments());
                    if (mode.equals("target")) {
                        args.putAll(target.attachArguments());
                        ConsoleLaunch.apply(args, true, 4299, 30, 60, 1000);
                    } else {
                        var command = new GeneralCommandLine("dotnet", folder.resolve("DolphinDotNet.Build.dll").toString(), "run", "--config", folder.resolve("dolphin-run.json").toString()).withWorkDirectory(file.getParent().toFile());
                        if (!dolphin.isBlank()) command.addParameters("--dolphin", dolphin);
                        if (mode.equals("emulator")) {
                            int port;
                            try (var socket = new ServerSocket(0, 0, java.net.InetAddress.getLoopbackAddress())) { port = socket.getLocalPort(); }
                            command.addParameters("--gdb-port", Integer.toString(port));
                            args.putAll(Map.of("transport", "tcp", "host", "127.0.0.1", "port", port, "connectTimeoutSeconds", 30));
                        }
                        emulator = command.createProcess();
                    }
                    indicator.checkCanceled();
                    if (!mode.equals("play")) args.put("stopOnEntry", false);
                    var ownedEmulator = emulator; var connectionLease = lease;
                    ApplicationManager.getApplication().invokeLater(() -> {
                        try {
                            if (environment.getProject().isDisposed() || result.isCancelled()) throw new ProcessCanceledException();
                            if (mode.equals("play")) {
                                var console = TextConsoleBuilderFactory.getInstance().createBuilder(environment.getProject()).getConsole();
                                var handler = new KillableProcessHandler(ownedEmulator, "DolphinDotNet emulator", java.nio.charset.StandardCharsets.UTF_8);
                                console.attachToProcess(handler);
                                var descriptor = new RunContentBuilder(new DefaultExecutionResult(console, handler), environment).showRunContent(environment.getContentToReuse());
                                Disposer.register(descriptor, () -> stopEmulator(ownedEmulator));
                                handler.addProcessListener(new ProcessAdapter() { @Override public void processWillTerminate(@NotNull ProcessEvent event, boolean destroy) { stopEmulator(ownedEmulator); } });
                                handler.startNotify(); result.setResult(descriptor);
                            } else {
                                var descriptor = AttachRunner.startSession(environment.getProject(), name, args, environment, () -> {
                                    stopEmulator(ownedEmulator); connectionLease.close();
                                }, ownedEmulator);
                                result.setResult(descriptor);
                            }
                        } catch (Exception e) { stopEmulator(ownedEmulator); if (connectionLease != null) connectionLease.close(); result.setError(e); }
                    });
                } catch (Exception e) { stopEmulator(emulator); if (lease != null) lease.close(); result.setError(e); }
            }
        });
        return result;
    }
    private static String runChecked(GeneralCommandLine command, ProgressIndicator indicator) throws ExecutionException {
        var handler = new CapturingProcessHandler(command);
        handler.addProcessListener(new ProcessAdapter() {
            @Override public void onTextAvailable(@NotNull ProcessEvent event, @NotNull com.intellij.openapi.util.Key outputType) {
                var line = event.getText().strip();
                if (!line.isEmpty()) indicator.setText2(line.length() > 200 ? line.substring(0, 200) : line);
            }
        });
        var output = handler.runProcessWithProgressIndicator(indicator);
        indicator.checkCanceled();
        if (output.getExitCode() != 0) throw new ExecutionException("Build preparation failed:\n" + output.getStdout() + output.getStderr());
        return output.getStdout();
    }
    static void stopEmulator(Process process) {
        if (process == null) return;
        // Only this launch's process tree; never close a pre-existing Dolphin window.
        process.descendants().forEach(ProcessHandle::destroy); process.destroy();
    }
}
