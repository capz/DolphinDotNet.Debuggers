using System.Text.Json;
using DolphinDotNet.Debugger;

namespace DolphinDotNet.DebugAdapter;

internal static class LauncherProbe
{
    // A single JSON settings argument avoids shell parsing of paths/hostnames.
    public static async Task<int> RunAsync(string settings)
    {
        try
        {
            using var json = JsonDocument.Parse(settings);
            var args = json.RootElement;
            var transport = AttachTransport.Parse(args);
            var endpoint = ConsoleLauncher.EndpointFrom(args, transport);
            var seconds = args.TryGetProperty("readyTimeoutSeconds", out var s) ? s.GetInt32() : 3;
            if (seconds is < 1 or > 120) throw new ArgumentException("readyTimeoutSeconds must be between 1 and 120.");
            await using var client = await LauncherClient.WaitForReadyAsync(endpoint, TimeSpan.FromSeconds(seconds));
            Console.WriteLine(JsonSerializer.Serialize(new { ready = true, maximumImageBytes = client.MaximumImageBytes,
                message = "Console ready to receive an ELF. Launch will check readiness again." }));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ready = false, message = error.Message }));
            return 1;
        }
    }
}
