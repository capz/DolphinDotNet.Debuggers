package com.dolphindotnet.rider;

import com.intellij.openapi.project.Project;
import com.intellij.openapi.wm.*;
import org.jetbrains.annotations.NotNull;

public final class TargetStatusWidgetFactory implements StatusBarWidgetFactory {
    @Override public @NotNull String getId() { return "DolphinDotNet.Target"; }
    @Override public @NotNull String getDisplayName() { return "GameCube target availability"; }
    @Override public boolean isAvailable(@NotNull Project project) { return DolphinProject.isProject(project); }
    @Override public @NotNull StatusBarWidget createWidget(@NotNull Project project) { return new TargetStatusWidget(project); }
}
