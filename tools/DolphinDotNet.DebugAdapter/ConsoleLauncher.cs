using System.Security.Cryptography;
using System.Text.Json;
using DolphinDotNet.Debugger;

namespace DolphinDotNet.DebugAdapter;

internal sealed record ConsoleLauncher(byte[] Image, LauncherEndpoint Endpoint, TimeSpan ReadyTimeout,
    TimeSpan UploadTimeout, int BootDelayMilliseconds)
{
    public static LauncherEndpoint EndpointFrom(JsonElement args, AttachTransport transport)
    {
        var port = args.TryGetProperty("launcherPort", out var p) ? p.GetInt32() : 4299;
        var endpoint = new LauncherEndpoint(transport.Kind, transport.Host, port, transport.SerialPort, transport.BaudRate);
        endpoint.Validate();
        return endpoint;
    }
    public static ConsoleLauncher Parse(JsonElement args, AttachTransport transport, SourceAddressMap symbols)
    {
        if (args.TryGetProperty("noDebug", out var noDebug) && noDebug.GetBoolean()) throw new ArgumentException("Launcher mode requires debugging; noDebug is unsupported.");
        if (args.TryGetProperty("programArguments", out var a) && (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() != 0))
            throw new ArgumentException("The minimal ELF launcher does not support programArguments.");
        var ready = args.TryGetProperty("readyTimeoutSeconds", out var r) ? r.GetInt32() : 30;
        var upload = args.TryGetProperty("uploadTimeoutSeconds", out var u) ? u.GetInt32() : 60;
        var delay = args.TryGetProperty("bootDelayMilliseconds", out var d) ? d.GetInt32() : 1000;
        if (ready is < 1 or > 120) throw new ArgumentException("readyTimeoutSeconds must be between 1 and 120.");
        // The native receiver bounds each session to 60 seconds.
        if (upload is < 1 or > 60) throw new ArgumentException("Launcher uploadTimeoutSeconds must be between 1 and 60.");
        if (delay is < 0 or > 30000) throw new ArgumentException("bootDelayMilliseconds must be between 0 and 30000.");
        var endpoint = EndpointFrom(args, transport);
        if (transport.Kind == "tcp" && endpoint.Port == transport.Port) throw new ArgumentException("Launcher upload port must differ from the application's GDB port.");
        using var file = File.OpenRead(args.GetProperty("elf").GetString()!);
        if (file.Length < 52 || file.Length > LauncherProtocol.MaximumImageBytes) throw new ArgumentException("Launcher ELF must be between 52 bytes and 16 MiB minus 4096 bytes.");
        var image = new byte[checked((int)file.Length)];
        file.ReadExactly(image);
        if (!image.AsSpan(0, 7).SequenceEqual("\u007fELF\u0001\u0002\u0001"u8)) throw new ArgumentException("Launcher requires a big-endian ELF32 artifact.");
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(image)), symbols.ElfSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ELF changed while preparing the upload. Rebuild and retry.");
        return new(image, endpoint, TimeSpan.FromSeconds(ready), TimeSpan.FromSeconds(upload), delay);
    }
    public async Task LaunchAsync(Func<string, Task> status, CancellationToken token)
    {
        await status("Waiting for DolphinDotNet.Launcher to report readiness to receive an ELF.\n");
        await using (var receiver = await LauncherClient.WaitForReadyAsync(Endpoint, ReadyTimeout, token))
        {
            await status($"Console READY TO RECEIVE ELF (limit {receiver.MaximumImageBytes} bytes). Uploading {Image.Length} bytes.\n");
            await receiver.UploadAndExecuteAsync(Image, UploadTimeout, cancellationToken: token,
                progress: (sent, total) =>
                {
                    var percent = (int)((long)sent * 100 / total);
                    var filled = percent / 5;
                    var bar = new string('#', filled) + new string('.', 20 - filled);
                    return status($"Upload [{bar}] {percent}% ({sent} / {total} bytes sent).\n");
                });
            await status("Console validated the ELF and acknowledged execution. Connecting to the application's GDB stub.\n");
        }
        await Task.Delay(BootDelayMilliseconds, token);
    }
}
