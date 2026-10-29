package com.dolphindotnet.rider;

import com.google.gson.Gson;
import com.intellij.execution.process.CapturingProcessHandler;
import com.intellij.openapi.application.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.wm.*;
import com.intellij.util.concurrency.AppExecutorUtil;
import org.jetbrains.annotations.NotNull;
import java.util.LinkedHashMap;
import java.util.concurrent.*;

final class TargetStatusWidget implements StatusBarWidget, StatusBarWidget.TextPresentation {
    private final Project project;
    private volatile boolean disposed;
    private volatile CapturingProcessHandler probe;
    private volatile String text = "Target GameCube not available", tooltip = "Configure a target using Attach to Nintendo GameCube → Manage targets.";
    private StatusBar bar;
    private ScheduledFuture<?> timer;
    TargetStatusWidget(Project project) { this.project = project; }
    @Override public @NotNull String ID() { return "DolphinDotNet.Target"; }
    @Override public WidgetPresentation getPresentation() { return this; }
    @Override public @NotNull String getText() { return text; }
    @Override public float getAlignment() { return 0; }
    @Override public String getTooltipText() { return tooltip; }
    @Override public void install(@NotNull StatusBar statusBar) {
        bar = statusBar;
        timer = AppExecutorUtil.getAppScheduledExecutorService().scheduleWithFixedDelay(this::poll, 1, 10, TimeUnit.SECONDS);
    }
    private void poll() {
        if (disposed || project.isDisposed()) return;
        try {
            var targets = TargetStore.getInstance().targets();
            if (targets.isEmpty()) { update("Target GameCube not available", "No saved target. Use Attach to Nintendo GameCube → Manage targets."); return; }
            var target = targets.getFirst();
            try (var lease = TargetConnectionGate.tryProbe()) {
                if (lease == null) { update("Target GameCube in use", "Readiness polling is suspended while connecting or debugging."); return; }
                var args = new LinkedHashMap<>(target.attachArguments());
                args.put("launcherPort", 4299); args.put("readyTimeoutSeconds", 1); args.put("timeoutSeconds", 1);
                var handler = new CapturingProcessHandler(AdapterCommand.create("--check-launcher", new Gson().toJson(args)));
                lease.holdUntilExit(handler.getProcess());
                probe = handler;
                if (disposed) handler.destroyProcess();
                try {
                    var output = handler.runProcess(4000);
                    var status = new Gson().fromJson(output.getStdout(), ProbeStatus.class);
                    boolean ready = !output.isTimeout() && status != null && status.ready;
                    // Discard results for settings that changed while the probe was running.
                    var current = TargetStore.getInstance().targets();
                    if (current.isEmpty() || !new Gson().toJson(current.getFirst()).equals(new Gson().toJson(target))) return;
                    update(ready ? "Target GameCube available" : "Target GameCube not available", target.name + ": " + (status == null ? "ELF launcher did not respond." : status.message));
                } finally { handler.destroyProcess(); handler.waitFor(1000); probe = null; }
            }
        } catch (Exception error) { update("Target GameCube not available", error.getMessage()); }
    }
    private void update(String label, String detail) {
        ApplicationManager.getApplication().invokeLater(() -> {
            if (disposed || project.isDisposed()) return;
            text = label; tooltip = detail; bar.updateWidget(ID());
        }, ModalityState.any());
    }
    @Override public void dispose() {
        disposed = true;
        if (timer != null) timer.cancel(true);
        var handler = probe; if (handler != null) handler.destroyProcess();
    }
    private static final class ProbeStatus { boolean ready; String message; }
}
