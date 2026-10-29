package com.dolphindotnet.rider;

import com.intellij.execution.*;
import com.intellij.execution.configurations.*;
import com.intellij.execution.executors.DefaultDebugExecutor;
import com.intellij.execution.runners.*;
import com.intellij.execution.ui.RunContentDescriptor;
import com.intellij.openapi.project.Project;
import org.jetbrains.annotations.Nullable;
import com.intellij.xdebugger.*;
import org.jetbrains.annotations.NotNull;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.progress.*;
import org.jetbrains.concurrency.*;

public final class AttachRunner extends AsyncProgramRunner<RunnerSettings> {
    @Override public @NotNull String getRunnerId() { return "DolphinDotNetAttachRunner"; }
    @Override public boolean canRun(@NotNull String executorId, @NotNull RunProfile profile) {
        return DefaultDebugExecutor.EXECUTOR_ID.equals(executorId) && profile instanceof AttachConfiguration;
    }
    @Override protected Promise<RunContentDescriptor> execute(@NotNull ExecutionEnvironment environment, @NotNull RunProfileState state) {
        return start(environment.getProject(), (AttachConfiguration)environment.getRunProfile(), environment);
    }
    static Promise<RunContentDescriptor> start(Project project, AttachConfiguration configuration, @Nullable ExecutionEnvironment environment) {
        var result = new AsyncPromise<RunContentDescriptor>();
        final java.util.Map<String, Object> args;
        try { args = configuration.attachArguments(); } catch (Exception e) { result.setError(e); return result; }
        ProgressManager.getInstance().run(new Task.Backgroundable(project, "Connecting to GameCube", true) {
            @Override public void run(@NotNull ProgressIndicator indicator) {
                try {
                    var lease = TargetConnectionGate.acquire(indicator::checkCanceled);
                    ApplicationManager.getApplication().invokeLater(() -> {
                        try {
                            if (project.isDisposed() || result.isCancelled()) throw new ProcessCanceledException();
                            result.setResult(startSession(project, configuration.getName(), args, environment, lease::close));
                        } catch (Exception e) { lease.close(); result.setError(e); }
                    });
                } catch (Exception e) { result.setError(e); }
            }
        });
        return result;
    }
    static RunContentDescriptor startSession(Project project, String name, java.util.Map<String,Object> args, @Nullable ExecutionEnvironment environment, Runnable cleanup) throws ExecutionException {
        return startSession(project, name, args, environment, cleanup, null);
    }
    static RunContentDescriptor startSession(Project project, String name, java.util.Map<String,Object> args, @Nullable ExecutionEnvironment environment, Runnable cleanup, @Nullable Process emulator) throws ExecutionException {
        var builder = XDebuggerManager.getInstance(project).newSessionBuilder(new XDebugProcessStarter() {
            @Override public @NotNull XDebugProcess start(@NotNull XDebugSession session) throws ExecutionException {
                return new RiderDebugProcess(session, AdapterCommand.create().createProcess(), args, cleanup, emulator);
            }
        }).sessionName(name).showTab(true);
        if (environment != null) builder.environment(environment);
        return builder.startSession().getRunContentDescriptor();
    }
}
