using System.Text.Json;
using DolphinDotNet.Debugger;

namespace DolphinDotNet.DebugAdapter;

internal sealed record UsbGeckoLaunch(WiiloadUpload Upload, TimeSpan Timeout, int BootDelayMilliseconds)
{
    public static UsbGeckoLaunch Parse(JsonElement args, AttachTransport transport, SourceAddressMap symbols)
    {
        if (transport.Kind != "usbGecko") throw new ArgumentException("Swiss launch requires transport=usbGecko.");
        if (args.TryGetProperty("noDebug", out var noDebug) && noDebug.GetBoolean())
            throw new ArgumentException("Swiss launch requires debugging; noDebug is unsupported.");
        var seconds = args.TryGetProperty("uploadTimeoutSeconds", out var t) ? t.GetInt32() : 60;
        if (seconds is < 1 or > 120) throw new ArgumentException("uploadTimeoutSeconds must be between 1 and 120.");
        var delay = args.TryGetProperty("bootDelayMilliseconds", out var d) ? d.GetInt32() : 1000;
        if (delay is < 0 or > 30000) throw new ArgumentException("bootDelayMilliseconds must be between 0 and 30000.");
        var arguments = args.TryGetProperty("programArguments", out var a)
            ? a.EnumerateArray().Select(value => value.GetString() ?? throw new ArgumentException("programArguments must contain strings.")).ToArray()
            : Array.Empty<string>();
        var upload = WiiloadUpload.FromFile(args.GetProperty("elf").GetString()!, arguments);
        if (!string.Equals(upload.ImageSha256, symbols.ElfSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ELF changed while preparing the upload. Rebuild and retry.");
        return new(upload, TimeSpan.FromSeconds(seconds), delay);
    }
}
