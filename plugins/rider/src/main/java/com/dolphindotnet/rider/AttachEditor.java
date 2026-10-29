package com.dolphindotnet.rider;

import com.intellij.openapi.components.PathMacroManager;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.execution.process.CapturingProcessHandler;
import com.google.gson.Gson;
import com.intellij.openapi.fileChooser.FileChooserDescriptorFactory;
import com.intellij.openapi.options.*;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.ui.*;
import com.intellij.ui.*;
import com.intellij.ui.components.*;
import com.intellij.util.ui.JBUI;
import javax.swing.*;
import javax.swing.table.DefaultTableModel;
import java.awt.*;
import java.nio.file.*;
import java.util.*;
import org.jetbrains.annotations.NotNull;

final class AttachEditor extends SettingsEditor<AttachConfiguration> {
    private final Project project;
    private final TextFieldWithBrowseButton folder = new TextFieldWithBrowseButton();
    private final ComboBox<TargetProfile> targets = new ComboBox<>();
    private final JBLabel elf = new JBLabel(), source = new JBLabel(), addresses = new JBLabel(), summary = new JBLabel();
    private final DefaultTableModel mappings = new DefaultTableModel(new String[]{"Build source prefix", "Local source prefix"}, 0);
    private final JTable table = new com.intellij.ui.table.JBTable(mappings);
    private String missingId;
    private final ComboBox<String> launchMode = new ComboBox<>(new String[]{"Attach to running build", "Upload with Swiss (USB Gecko)", "DolphinDotNet ELF launcher (BBA / USB Gecko)"});
    private final JSpinner uploadTimeout = new JSpinner(new SpinnerNumberModel(60, 1, 120, 1));
    private final JSpinner bootDelay = new JSpinner(new SpinnerNumberModel(1000, 0, 30000, 100));
    private final JSpinner launcherPort = new JSpinner(new SpinnerNumberModel(4299, 1, 65535, 1));
    private final JSpinner readyTimeout = new JSpinner(new SpinnerNumberModel(30, 1, 120, 1));
    private final JButton checkReady = new JButton("Check receiver readiness");
    private final JBLabel readiness = new JBLabel("Not checked; launch checks readiness again.");
    private volatile CapturingProcessHandler probe;
    private volatile boolean disposed;
    private boolean checking;
    private int readinessRevision;
    AttachEditor(Project project) { this.project = project; }
    @Override protected @NotNull JComponent createEditor() {
        var panel = Forms.form(); panel.setBorder(JBUI.Borders.empty(12));
        folder.addBrowseFolderListener(project, FileChooserDescriptorFactory.createSingleFolderDescriptor().withTitle("Debug build folder"));
        folder.getTextField().getDocument().addDocumentListener(new DocumentAdapter() { @Override protected void textChanged(@NotNull javax.swing.event.DocumentEvent e) { updateArtifacts(); } });
        Forms.row(panel, 0, "Debug build folder:", folder); Forms.row(panel, 1, "ELF:", elf);
        Forms.row(panel, 2, "Source map:", source); Forms.row(panel, 3, "Address map:", addresses);
        Forms.row(panel, 4, "", new TitledSeparator("Target connection"));
        var manage = new JButton("Manage targets…");
        manage.addActionListener(e -> {
            var selected = (TargetProfile)targets.getSelectedItem();
            var dialog = new TargetDialog(project, selected == null ? missingId : selected.id);
            if (dialog.showAndGet()) reloadTargets(dialog.selectedId());
        });
        targets.addActionListener(e -> updateSummary());
        Forms.row(panel, 5, "Target:", Forms.beside(targets, manage)); Forms.row(panel, 6, "", summary);
        var mappingPanel = new JPanel(new BorderLayout());
        var mappingContent = new JPanel(new BorderLayout()); mappingContent.add(new JBScrollPane(table));
        var add = new JButton("+"); var remove = new JButton("−");
        add.addActionListener(e -> mappings.addRow(new Object[]{"", ""}));
        remove.addActionListener(e -> { if (table.getSelectedRow() >= 0) mappings.removeRow(table.getSelectedRow()); });
        var toolbar = new JPanel(new FlowLayout(FlowLayout.LEFT)); toolbar.add(add); toolbar.add(remove); mappingContent.add(toolbar, BorderLayout.SOUTH);
        mappingContent.setPreferredSize(JBUI.size(500, 150));
        var hideable = new HideableDecorator(mappingPanel, "Source path mappings", false); hideable.setContentComponent(mappingContent);
        Forms.row(panel, 7, "", mappingPanel);
        Forms.row(panel, 8, "Start on device:", launchMode);
        Forms.row(panel, 9, "Upload timeout (seconds):", uploadTimeout);
        Forms.row(panel, 10, "Boot delay (milliseconds):", bootDelay);
        Forms.row(panel, 11, "Launcher TCP upload port:", launcherPort);
        Forms.row(panel, 12, "Readiness wait (seconds):", readyTimeout);
        var showLauncher = new JButton("Open bundled launcher folder");
        showLauncher.addActionListener(e -> {
            try { com.intellij.ide.actions.RevealFileAction.openDirectory(AdapterCommand.launcherDirectory()); }
            catch (Exception error) { Messages.showErrorDialog(project, error.getMessage(), "Console Launcher"); }
        });
        Forms.row(panel, 13, "Console receiver:", Forms.beside(checkReady, showLauncher));
        Forms.row(panel, 14, "Readiness:", readiness);
        Forms.row(panel, 15, "", new JBLabel("Boot our ELF launcher or Swiss. The application needs a waiting GDB stub matching the target."));
        launchMode.addActionListener(e -> updateSummary());
        launcherPort.addChangeListener(e -> invalidateReadiness()); readyTimeout.addChangeListener(e -> invalidateReadiness());
        checkReady.addActionListener(e -> checkReadiness());
        var root = new JPanel(new BorderLayout()); root.add(panel, BorderLayout.NORTH); return root;
    }
    @Override protected void resetEditorFrom(@NotNull AttachConfiguration config) {
        folder.setText(config.buildFolder); reloadTargets(config.targetId); mappings.setRowCount(0);
        launchMode.setSelectedIndex(config.useDolphinDotNetLauncher ? 2 : config.uploadWithSwiss ? 1 : 0);
        uploadTimeout.setValue(config.uploadTimeoutSeconds); bootDelay.setValue(config.bootDelayMilliseconds);
        launcherPort.setValue(config.launcherPort); readyTimeout.setValue(config.readyTimeoutSeconds); updateSummary();
        config.sourceFileMap.forEach((build, local) -> mappings.addRow(new Object[]{build, local})); updateArtifacts();
    }
    @Override protected void applyEditorTo(@NotNull AttachConfiguration config) throws ConfigurationException {
        if (table.isEditing() && !table.getCellEditor().stopCellEditing()) throw new ConfigurationException("Finish editing source mappings.");
        var paths = new LinkedHashMap<String, String>();
        for (int i = 0; i < mappings.getRowCount(); i++) {
            var build = Objects.toString(mappings.getValueAt(i, 0), "").trim(); var local = Objects.toString(mappings.getValueAt(i, 1), "").trim();
            if (build.isEmpty() || local.isEmpty() || paths.put(build, local) != null) throw new ConfigurationException("Source mappings need unique build prefixes and nonempty local paths.");
        }
        config.buildFolder = folder.getText().trim();
        var target = (TargetProfile)targets.getSelectedItem(); config.targetId = target == null ? Objects.toString(missingId, "") : target.id;
        config.sourceFileMap.clear(); config.sourceFileMap.putAll(paths);
        config.uploadWithSwiss = launchMode.getSelectedIndex()==1; config.useDolphinDotNetLauncher = launchMode.getSelectedIndex()==2;
        config.uploadTimeoutSeconds = (Integer)uploadTimeout.getValue(); config.bootDelayMilliseconds = (Integer)bootDelay.getValue();
        config.launcherPort = (Integer)launcherPort.getValue(); config.readyTimeoutSeconds = (Integer)readyTimeout.getValue();
    }
    private void reloadTargets(String id) {
        missingId = id; targets.removeAllItems(); TargetProfile selected = null;
        for (var t : TargetStore.getInstance().targets()) { targets.addItem(t); if (Objects.equals(t.id, id)) selected = t; }
        targets.setSelectedItem(selected); updateSummary();
    }
    private void updateSummary() {
        var t = (TargetProfile)targets.getSelectedItem();
        summary.setText(t == null ? "Select a target or define one using Manage targets…" : t.summary());
        if (t!=null && !"usbGecko".equals(t.transport) && launchMode.getSelectedIndex()==1) launchMode.setSelectedIndex(0);
        boolean nativeLauncher=launchMode.getSelectedIndex()==2;
        var model=(SpinnerNumberModel)uploadTimeout.getModel(); model.setMaximum(nativeLauncher ? 60 : 120);
        if (nativeLauncher && (Integer)uploadTimeout.getValue()>60) uploadTimeout.setValue(60);
        uploadTimeout.setEnabled(launchMode.getSelectedIndex()>0); bootDelay.setEnabled(launchMode.getSelectedIndex()>0);
        launcherPort.setEnabled(nativeLauncher && t!=null && "tcp".equals(t.transport));
        readyTimeout.setEnabled(nativeLauncher);checkReady.setEnabled(nativeLauncher && t!=null && !checking);
        invalidateReadiness();
    }
    private void invalidateReadiness() { readinessRevision++;readiness.setText("Not checked; launch checks readiness again."); }
    private void checkReadiness() {
        if (checking) return;
        try {
            var target=(TargetProfile)targets.getSelectedItem();
            if (target==null) throw new IllegalArgumentException("Select a target first.");
            var args=new LinkedHashMap<String,Object>(target.attachArguments());
            int port=(Integer)launcherPort.getValue();
            if (port<1 || port>65535) throw new IllegalArgumentException("Choose a valid upload port.");
            args.put("launcherPort",port);args.put("readyTimeoutSeconds",Math.min(3,(Integer)readyTimeout.getValue()));
            var command=AdapterCommand.create("--check-launcher",new Gson().toJson(args));
            int revision=readinessRevision;checking=true;checkReady.setEnabled(false);readiness.setText("Waiting for receiver...");
            ApplicationManager.getApplication().executeOnPooledThread(() -> {
                String message;
                try (var lease=TargetConnectionGate.tryProbe()) {
                    if (lease==null) throw new IllegalStateException("Target connection is in use. Stop debugging before checking readiness.");
                    var handler=new CapturingProcessHandler(command);probe=handler;
                    if (disposed) handler.destroyProcess();
                    com.intellij.execution.process.ProcessOutput result;
                    try { result=handler.runProcess(10000); }
                    finally { handler.destroyProcess();handler.waitFor(1000); }
                    if (result.isTimeout()) throw new IllegalStateException("Receiver check timed out.");
                    var reply=new Gson().fromJson(result.getStdout(),ProbeStatus.class);
                    if (reply==null || reply.message==null) throw new IllegalStateException("Invalid receiver status. " + result.getStderr());
                    message=reply.message;
                } catch (Exception error) { message=error.getMessage(); }
                finally { probe=null; }
                String text=message;
                // Only update controls: the attach editor may be in a modal dialog.
                ApplicationManager.getApplication().invokeLater(() -> {
                    if (disposed) return;
                    checking=false;checkReady.setEnabled(launchMode.getSelectedIndex()==2 && targets.getSelectedItem()!=null);
                    readiness.setText(revision==readinessRevision ? text : "Settings changed; check again.");
                }, ModalityState.any());
            });
        } catch (Exception error) { readiness.setText(error.getMessage()); }
    }
    private static final class ProbeStatus { boolean ready; String message; }
    @Override protected void disposeEditor() { disposed=true;var handler=probe;if (handler!=null) handler.destroyProcess(); }
    private void updateArtifacts() {
        try {
            var found = BuildArtifacts.discover(Path.of(PathMacroManager.getInstance(project).expandPath(folder.getText())));
            elf.setText(found.elf().getFileName().toString()); source.setText(found.sourceMap().getFileName().toString()); addresses.setText(found.addressMap().getFileName().toString());
        } catch (Exception e) { elf.setText("—"); source.setText("—"); addresses.setText("—"); elf.setToolTipText(e.getMessage()); }
    }
}
