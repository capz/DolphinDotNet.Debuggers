package com.dolphindotnet.rider;

import com.intellij.openapi.components.*;
import com.intellij.openapi.project.Project;
import org.jetbrains.annotations.NotNull;
import java.util.LinkedHashMap;
import java.util.Map;

/** Machine-local per-project dialog preferences, independent of RunManager selection. */
@Service(Service.Level.PROJECT)
@State(name = "DolphinDotNetAttach", storages = @Storage(StoragePathMacros.WORKSPACE_FILE))
public final class AttachSettings implements PersistentStateComponent<AttachSettings.Data> {
    public static final class Data {
        public boolean configured;
        public boolean uploadWithSwiss;
        public boolean useDolphinDotNetLauncher;
        public int launcherPort = 4299, readyTimeoutSeconds = 30;
        public int uploadTimeoutSeconds = 60, bootDelayMilliseconds = 1000;
        public String buildFolder = "$PROJECT_DIR$/bin/Debug/net8.0", targetId = "";
        public Map<String, String> sourceFileMap = new LinkedHashMap<>();
    }
    private volatile Data data = new Data();
    static AttachSettings getInstance(Project project) { return project.getService(AttachSettings.class); }
    @Override public @NotNull Data getState() { return data; }
    @Override public void loadState(@NotNull Data state) { data = state; }
    void restore(AttachConfiguration config) {
        config.buildFolder = data.buildFolder; config.targetId = data.targetId;
        config.uploadWithSwiss = data.uploadWithSwiss; config.uploadTimeoutSeconds = data.uploadTimeoutSeconds; config.bootDelayMilliseconds = data.bootDelayMilliseconds;
        config.useDolphinDotNetLauncher = data.useDolphinDotNetLauncher; config.launcherPort = data.launcherPort; config.readyTimeoutSeconds = data.readyTimeoutSeconds;
        config.sourceFileMap.clear(); config.sourceFileMap.putAll(data.sourceFileMap);
    }
    void remember(AttachConfiguration config) {
        var next = new Data(); next.configured = true;
        next.buildFolder = config.buildFolder; next.targetId = config.targetId;
        next.uploadWithSwiss = config.uploadWithSwiss; next.uploadTimeoutSeconds = config.uploadTimeoutSeconds; next.bootDelayMilliseconds = config.bootDelayMilliseconds;
        next.useDolphinDotNetLauncher = config.useDolphinDotNetLauncher; next.launcherPort = config.launcherPort; next.readyTimeoutSeconds = config.readyTimeoutSeconds;
        next.sourceFileMap.putAll(config.sourceFileMap); data = next;
    }
}
