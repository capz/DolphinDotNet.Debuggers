package com.dolphindotnet.rider;

import com.intellij.execution.configurations.*;
import com.intellij.icons.AllIcons;
import com.intellij.openapi.project.Project;
import org.jetbrains.annotations.NotNull;

public final class LaunchConfigurationType extends ConfigurationTypeBase {
    public LaunchConfigurationType() {
        super("DolphinDotNetLaunch", "DolphinDotNet", "Build and run in Dolphin or debug on a GameCube", AllIcons.RunConfigurations.Application);
        addFactory(new ConfigurationFactory(this) {
            @Override public @NotNull String getId() { return "DolphinDotNetLaunch"; }
            @Override public @NotNull RunConfiguration createTemplateConfiguration(@NotNull Project project) {
                return new LaunchConfiguration(project, this, "Run in emulator");
            }
        });
    }
}
