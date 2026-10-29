package com.dolphindotnet.rider;

import com.intellij.execution.*;
import com.intellij.execution.configurations.*;
import com.intellij.execution.runners.ExecutionEnvironment;
import com.intellij.openapi.components.PathMacroManager;
import com.intellij.openapi.options.SettingsEditor;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.ui.*;
import org.jdom.Element;
import org.jetbrains.annotations.NotNull;
import javax.swing.*;
import java.nio.file.*;
import java.util.*;

public final class LaunchConfiguration extends RunConfigurationBase<Element> {
    String mode = "play", projectFile = "", dolphin = "";
    LaunchConfiguration(Project project, ConfigurationFactory factory, String name) { super(project, factory, name); }
    Path projectFile() {
        if (!Set.of("play", "emulator", "target").contains(mode)) throw new IllegalArgumentException("Choose a launch mode.");
        var path = Path.of(PathMacroManager.getInstance(getProject()).expandPath(projectFile)).toAbsolutePath();
        if (!Files.isRegularFile(path) || !path.toString().endsWith(".csproj")) throw new IllegalArgumentException("Choose the sample's .csproj file.");
        return path;
    }
    @Override public void checkConfiguration() throws RuntimeConfigurationException {
        try {
            projectFile();
            if (mode.equals("target")) firstTarget();
        } catch (Exception e) { throw new RuntimeConfigurationError(e.getMessage()); }
    }
    static TargetProfile firstTarget() {
        var targets = TargetStore.getInstance().targets();
        if (targets.isEmpty()) throw new IllegalArgumentException("Configure a target in Attach to Nintendo GameCube → Manage targets first.");
        var target = targets.getFirst(); target.validate(); return target;
    }
    @Override public @NotNull SettingsEditor<? extends RunConfiguration> getConfigurationEditor() {
        return new SettingsEditor<LaunchConfiguration>() {
            final ComboBox<String> modes = new ComboBox<>(new String[]{"Run in emulator", "Debug on emulator", "Debug on target"});
            final JTextField file = new JTextField(), executable = new JTextField();
            @Override protected @NotNull JComponent createEditor() {
                var panel = Forms.form();
                Forms.row(panel, 0, "Action:", modes); Forms.row(panel, 1, "Project file:", file);
                Forms.row(panel, 2, "Dolphin executable (optional):", executable);
                Forms.row(panel, 3, "", new JLabel("Hardware debugging uses the first saved target and the DolphinDotNet ELF launcher."));
                return panel;
            }
            @Override protected void resetEditorFrom(@NotNull LaunchConfiguration c) {
                modes.setSelectedIndex(c.mode.equals("target") ? 2 : c.mode.equals("emulator") ? 1 : 0);
                file.setText(c.projectFile); executable.setText(c.dolphin);
            }
            @Override protected void applyEditorTo(@NotNull LaunchConfiguration c) {
                c.mode = new String[]{"play", "emulator", "target"}[modes.getSelectedIndex()];
                c.projectFile = file.getText().trim(); c.dolphin = executable.getText().trim();
            }
        };
    }
    @Override public RunProfileState getState(@NotNull Executor executor, @NotNull ExecutionEnvironment environment) {
        return (e, runner) -> null;
    }
    @Override public void readExternal(@NotNull Element e) {
        super.readExternal(e); mode = e.getAttributeValue("mode", "play"); projectFile = e.getAttributeValue("projectFile", ""); dolphin = e.getAttributeValue("dolphin", "");
    }
    @Override public void writeExternal(@NotNull Element e) {
        super.writeExternal(e); e.setAttribute("mode", mode); e.setAttribute("projectFile", projectFile); e.setAttribute("dolphin", dolphin);
    }
}
