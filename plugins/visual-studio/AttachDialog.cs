using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.VisualStudio.Shell;

namespace DolphinDotNet.VisualStudio;

internal sealed class AttachDialog : Form
{
    private readonly TextBox build = new TextBox(), host = new TextBox(), serial = new TextBox(), mappings = new TextBox(), heartbeat = new TextBox();
    private readonly NumericUpDown port = Number(1,65535), baud = Number(1,2000000), timeout = Number(1,120), heartbeatPort = Number(0,65535);
    private readonly ComboBox transport = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox launchMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown uploadTimeout = Number(1,120), bootDelay = Number(0,30000);
    private readonly NumericUpDown launcherPort = Number(1,65535), readyTimeout = Number(1,120);
    private readonly Button checkReady = new Button { Text = "Check receiver readiness", AutoSize = true };
    private readonly Label readiness = new Label { Text = "Not checked", AutoSize = true, MaximumSize = new Size(480,0) };
    private Process? probe;
    private int probeVersion;
    private readonly string adapter;
    public AttachOptions Options { get; private set; }
    public AttachDialog(AttachOptions options, string adapter)
    {
        Options = options;this.adapter = adapter;
        Text = "Attach to DolphinDotNet";StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(640,620);Size = new Size(720,680);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        Controls.Add(layout);
        var folder = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true };
        folder.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folder.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,85));
        var browse = new Button { Text = "Browse…", Dock = DockStyle.Fill };
        browse.Click += (_, __) => { using (var picker = new FolderBrowserDialog()) { picker.SelectedPath = build.Text;if (picker.ShowDialog(this)==DialogResult.OK) build.Text=picker.SelectedPath; } };
        folder.Controls.Add(build,0,0);folder.Controls.Add(browse,1,0);
        Add(layout,"Debug build folder",folder);
        transport.Items.AddRange(new object[] { "BBA / TCP", "USB Gecko" });transport.SelectedIndex = options.Transport=="usbGecko" ? 1 : 0;
        Add(layout,"Transport",transport);Add(layout,"Host",host);Add(layout,"TCP port",port);
        Add(layout,"Serial port",serial);Add(layout,"Driver baud rate",baud);Add(layout,"Timeout (seconds)",timeout);
        launchMode.Items.AddRange(new object[] { "Attach to running build", "Upload with Swiss (USB Gecko)", "DolphinDotNet ELF launcher (BBA / USB Gecko)" });
        launchMode.SelectedIndex=options.UseDolphinDotNetLauncher ? 2 : options.UploadWithSwiss ? 1 : 0;
        Add(layout,"Start on device",launchMode);Add(layout,"Upload timeout (seconds)",uploadTimeout);Add(layout,"Boot delay (milliseconds)",bootDelay);
        Add(layout,"Launcher TCP upload port",launcherPort);Add(layout,"Readiness wait (seconds)",readyTimeout);
        var showLauncher = new Button { Text = "Open bundled launcher folder", AutoSize = true };
        showLauncher.Click += (_, __) => {
            try {
                var directory=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(adapter)!,"..","launcher"));
                if (!File.Exists(Path.Combine(directory,"DolphinDotNet.Launcher.dol"))) throw new FileNotFoundException("The extension package is missing its console launcher.");
                Process.Start(new ProcessStartInfo(directory) { UseShellExecute=true });
            } catch (Exception error) { MessageBox.Show(this,error.Message,"Console launcher",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        };
        var receiverButtons = new FlowLayoutPanel { AutoSize=true, Dock=DockStyle.Fill };
        receiverButtons.Controls.Add(checkReady);receiverButtons.Controls.Add(showLauncher);
        Add(layout,"Console receiver",receiverButtons);Add(layout,"Readiness",readiness);
        Add(layout,"Live status IPv4 (optional)",heartbeat);Add(layout,"Live status UDP port",heartbeatPort);
        mappings.Multiline = true;mappings.ScrollBars = ScrollBars.Both;mappings.Height = 100;
        Add(layout,"Source mappings (JSON)",mappings);
        var help = new Label { Text = "For our ELF launcher, boot DolphinDotNet.Launcher on the console. Check readiness without uploading; launch checks again and waits for the receiver. Upload and GDB TCP ports are separate. The application needs a waiting GDB stub matching the chosen transport.", AutoSize = true, MaximumSize = new Size(500,0) };
        Add(layout,"",help);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var attach = new Button { Text = "Attach", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        attach.Click += (_, __) => Save();buttons.Controls.Add(attach);buttons.Controls.Add(cancel);Add(layout,"",buttons);
        AcceptButton = attach;CancelButton = cancel;
        build.Text=options.BuildFolder;host.Text=options.Host;port.Value=Bound(options.Port,port);
        serial.Text=options.SerialPort;baud.Value=Bound(options.BaudRate,baud);timeout.Value=Bound(options.TimeoutSeconds,timeout);
        mappings.Text=options.SourceFileMapJson;heartbeat.Text=options.HeartbeatHost;heartbeatPort.Value=Bound(options.HeartbeatPort,heartbeatPort);
        uploadTimeout.Value=Bound(options.UploadTimeoutSeconds,uploadTimeout);bootDelay.Value=Bound(options.BootDelayMilliseconds,bootDelay);
        launcherPort.Value=Bound(options.LauncherPort,launcherPort);readyTimeout.Value=Bound(options.ReadyTimeoutSeconds,readyTimeout);
        launchMode.SelectedIndexChanged += (_, __) => UpdateTransport();
        host.TextChanged += (_, __) => InvalidateProbe();serial.TextChanged += (_, __) => InvalidateProbe();
        port.ValueChanged += (_, __) => InvalidateProbe();baud.ValueChanged += (_, __) => InvalidateProbe();launcherPort.ValueChanged += (_, __) => InvalidateProbe();
        checkReady.Click += (_, __) => CheckReadyAsync().FileAndForget("DolphinDotNet/LauncherProbe");
        transport.SelectedIndexChanged += (_, __) => UpdateTransport();UpdateTransport();
    }
    private void Save()
    {
        try
        {
            var value = ReadOptions();
            value.CreateLaunch(adapter);Options=value;DialogResult=DialogResult.OK;Close();
        }
        catch (Exception error) { MessageBox.Show(this,error.Message,"Check attach settings",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
    }
    private void UpdateTransport() {
        host.Enabled=port.Enabled=transport.SelectedIndex==0;serial.Enabled=baud.Enabled=transport.SelectedIndex==1;
        if (transport.SelectedIndex==0 && launchMode.SelectedIndex==1) launchMode.SelectedIndex=0;
        uploadTimeout.Maximum=launchMode.SelectedIndex==2 ? 60 : 120;
        uploadTimeout.Enabled=bootDelay.Enabled=launchMode.SelectedIndex>0;
        launcherPort.Enabled=launchMode.SelectedIndex==2 && transport.SelectedIndex==0;
        readyTimeout.Enabled=checkReady.Enabled=launchMode.SelectedIndex==2 && probe==null;
        InvalidateProbe();
    }
    private AttachOptions ReadOptions() => new AttachOptions { BuildFolder=build.Text.Trim(), Transport=transport.SelectedIndex==0 ? "tcp" : "usbGecko",
        Host=host.Text.Trim(),Port=(int)port.Value,SerialPort=serial.Text.Trim(),BaudRate=(int)baud.Value,TimeoutSeconds=(int)timeout.Value,
        SourceFileMapJson=mappings.Text,HeartbeatHost=heartbeat.Text.Trim(),HeartbeatPort=(int)heartbeatPort.Value,
        UploadWithSwiss=launchMode.SelectedIndex==1,UseDolphinDotNetLauncher=launchMode.SelectedIndex==2,
        UploadTimeoutSeconds=(int)uploadTimeout.Value,BootDelayMilliseconds=(int)bootDelay.Value,
        LauncherPort=(int)launcherPort.Value,ReadyTimeoutSeconds=(int)readyTimeout.Value };
    private void InvalidateProbe() { probeVersion++;readiness.Text="Not checked; launch checks current readiness."; }
    private async Task CheckReadyAsync()
    {
        if (probe!=null) return;
        int version=probeVersion;
        try
        {
            var settings=ReadOptions().CreateLauncherProbe().ToString(Formatting.None);
            checkReady.Enabled=false;((Button)AcceptButton).Enabled=false;readiness.Text="Waiting for receiver...";
            using (var process=new Process { StartInfo=new ProcessStartInfo(adapter,"--check-launcher-stdin") {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true } })
            {
                probe=process;
                process.Start();
                var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
                await process.StandardInput.WriteAsync(settings);process.StandardInput.Close();
                if (!await Task.Run(()=>process.WaitForExit(10000))) { process.Kill();throw new TimeoutException("Receiver check timed out."); }
                var text=await output;await errors;
                if (!IsDisposed) readiness.Text=version==probeVersion ? (string?)JObject.Parse(text)["message"] ?? "Invalid receiver status." : "Settings changed; check again.";
            }
        }
        catch (Exception error) { if (!IsDisposed) readiness.Text=error.Message; }
        finally { probe=null;if (!IsDisposed) { checkReady.Enabled=launchMode.SelectedIndex==2;((Button)AcceptButton).Enabled=true; } }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && probe!=null) { try { if (!probe.HasExited) probe.Kill(); } catch (InvalidOperationException) { } }
        base.Dispose(disposing);
    }
    private static NumericUpDown Number(int min,int max) => new NumericUpDown { Minimum=min,Maximum=max };
    private static decimal Bound(int value,NumericUpDown control) => Math.Max(control.Minimum,Math.Min(control.Maximum,value));
    private static void Add(TableLayoutPanel layout,string label,Control control)
    {
        var row=layout.RowCount++;layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text=label,AutoSize=true,Margin=new Padding(0,7,8,7) },0,row);
        control.Dock=DockStyle.Fill;control.Margin=new Padding(0,3,0,3);layout.Controls.Add(control,1,row);
    }
}
