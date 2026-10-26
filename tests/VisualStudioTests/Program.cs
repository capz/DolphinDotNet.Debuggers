using DolphinDotNet.VisualStudio;
using Newtonsoft.Json.Linq;

var root = Path.Combine(Path.GetTempPath(), "dnd-vs-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var folder = Path.Combine(root, "Debug build 日本");Directory.CreateDirectory(folder);
    var adapter = Path.Combine(root, "adapter.exe");File.WriteAllText(adapter, "fixture");
    var elf = Path.Combine(folder,"Snake.elf");File.WriteAllText(elf,"fixture");
    var source = Path.Combine(folder,"Snake.dnd-debug.json");File.WriteAllText(source,"{}");
    var addresses = Path.Combine(folder,"Snake.dnd-addresses.json");File.WriteAllText(addresses,"{}");
    var options = new AttachOptions { BuildFolder=folder,SourceFileMapJson="{\"/build/Game\":\"D:/Projects/Game\"}" };
    var launch = options.CreateLaunch(adapter);
    Check((string?)launch["request"]=="attach" && (string?)launch["$adapter"]==adapter, "attach command");
    Check((string?)launch["sourceMap"]==source && (string?)launch["elf"]==elf, "artifacts with spaces/unicode");
    Check((string?)launch["sourceFileMap"]?["/build/Game"]=="D:/Projects/Game", "source mappings");
    Check((int?)launch["port"]==2166 && (string?)launch["host"]=="127.0.0.1", "emulator defaults");
    Check((string?)launch["$localsScopeName"]=="Locals", "native Locals window");
    options.Transport="usbGecko";options.SerialPort="COM8";
    launch=options.CreateLaunch(adapter);
    Check((string?)launch["serialPort"]=="COM8" && launch["port"]==null && (int?)launch["baudRate"]==115200,"USB arguments");
    options.UploadWithSwiss=true;options.UploadTimeoutSeconds=90;options.BootDelayMilliseconds=2500;
    launch=options.CreateLaunch(adapter);
    Check((string?)launch["request"]=="launch" && (int?)launch["uploadTimeoutSeconds"]==90 && (int?)launch["bootDelayMilliseconds"]==2500,"Swiss launch arguments");
    var launchSettings=Path.Combine(root,"launch-settings.json");options.Save(launchSettings);
    var restoredLaunch=AttachOptions.Load(launchSettings);
    Check(restoredLaunch.UploadWithSwiss && restoredLaunch.UploadTimeoutSeconds==90 && restoredLaunch.BootDelayMilliseconds==2500,"Swiss settings roundtrip");
    options.Transport="tcp";Reject(()=>options.CreateLaunch(adapter));options.Transport="usbGecko";
    options.UploadTimeoutSeconds=0;Reject(()=>options.CreateLaunch(adapter));options.UploadTimeoutSeconds=60;
    options.BootDelayMilliseconds=30001;Reject(()=>options.CreateLaunch(adapter));options.BootDelayMilliseconds=1000;
    options.UploadWithSwiss=false;
    options.UseDolphinDotNetLauncher=true;options.Transport="tcp";options.LauncherPort=4299;options.ReadyTimeoutSeconds=45;
    launch=options.CreateLaunch(adapter);
    Check((string?)launch["launcher"]=="dolphinDotNet" && (string?)launch["request"]=="launch" && (int?)launch["launcherPort"]==4299 && (int?)launch["readyTimeoutSeconds"]==45,"native launcher configuration");
    Check((int?)options.CreateLauncherProbe()["readyTimeoutSeconds"]==3,"quick readiness check independent of build");
    options.Save(launchSettings);restoredLaunch=AttachOptions.Load(launchSettings);
    Check(restoredLaunch.UseDolphinDotNetLauncher && restoredLaunch.ReadyTimeoutSeconds==45 && restoredLaunch.LauncherPort==4299,"receiver settings persistence");
    options.LauncherPort=options.Port;Reject(()=>options.CreateLaunch(adapter));options.LauncherPort=4299;
    options.UploadWithSwiss=true;Reject(()=>options.CreateLaunch(adapter));options.UploadWithSwiss=false;
    options.ReadyTimeoutSeconds=0;Reject(()=>options.CreateLaunch(adapter));options.ReadyTimeoutSeconds=30;
    options.UploadTimeoutSeconds=61;Reject(()=>options.CreateLaunch(adapter));options.UploadTimeoutSeconds=60;
    options.Transport="usbGecko";
    Check((string?)options.CreateLaunch(adapter)["serialPort"]=="COM8","native USB launcher");
    Check((string?)options.CreateLauncherProbe()["serialPort"]=="COM8","USB readiness configuration");
    options.UseDolphinDotNetLauncher=false;
    options.HeartbeatHost="192.168.1.8";options.HeartbeatPort=0;
    Check((int?)options.CreateLaunch(adapter)["heartbeatPort"]==0,"automatic heartbeat port");
    options.HeartbeatHost="224.1.1.1";Reject(()=>options.CreateLaunch(adapter));options.HeartbeatHost="";
    options.Transport="tcp";options.Port=0;Reject(()=>options.CreateLaunch(adapter));options.Port=2166;
    options.TimeoutSeconds=121;Reject(()=>options.CreateLaunch(adapter));options.TimeoutSeconds=10;
    options.Transport="other";Reject(()=>options.CreateLaunch(adapter));options.Transport="tcp";
    options.SourceFileMapJson="{\"/build\":12}";Reject(()=>options.CreateLaunch(adapter));options.SourceFileMapJson="{}";
    File.Delete(source);Reject(()=>options.CreateLaunch(adapter));
    var fallback=Path.Combine(folder,"dnd-il-debug.json");File.WriteAllText(fallback,"{}");
    Check((string?)options.CreateLaunch(adapter)["sourceMap"]==fallback,"native bundle source map");
    File.WriteAllText(Path.Combine(folder,"Other.elf"),"fixture");Reject(()=>options.CreateLaunch(adapter));File.Delete(Path.Combine(folder,"Other.elf"));
    var saved=Path.Combine(root,".vs","DolphinDotNet","attach.json");options.Save(saved);
    var restored=AttachOptions.Load(saved);Check(restored.BuildFolder==folder && restored.Port==2166,"settings roundtrip");
    Check(AttachOptions.Load(Path.Combine(root,"missing.json")).Port==2166,"first-use defaults");
    Console.WriteLine("Visual Studio attach settings, artifact discovery, transport selection, source mappings and persistence passed.");
}
finally { Directory.Delete(root,true); }
static void Check(bool value,string message) { if(!value)throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch(ArgumentException) { return; } throw new Exception("Invalid configuration accepted"); }
