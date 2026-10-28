using System.Net;
using System.Net.Sockets;
using System.Text;
using DolphinDotNet.Debugger;

internal static class LibogcRegisterTests
{
    internal static async Task RunAsync()
    {
        var bytes = Enumerable.Range(0, 412).Select(i => (byte)(i * 17 + 3)).ToArray();
        string packet = Convert.ToHexString(bytes);
        var registers = new[] { (0, 0, 4), (1, 4, 4), (31, 124, 4), (32, 128, 8),
            (63, 376, 8), (64, 384, 4), (65, 388, 4), (66, 392, 4), (67, 396, 4), (68, 400, 4), (69, 404, 4), (70, 408, 4) };
        var script = new List<(string, string)> { ("p0", "") };
        script.AddRange(registers.Select(_ => ("g", packet)));
        await WithPeerAsync(script, async client =>
        {
            foreach (var (index, offset, size) in registers)
                Require((await client.ReadRegisterAsync(index)).SequenceEqual(bytes.AsSpan(offset, size).ToArray()), "PowerPC register offset/width " + index);
        });
        // Never reuse a bulk snapshot after a step or execution change.
        string changed = new string('a', 824);
        await WithPeerAsync(new[] { ("p1", ""), ("g", packet), ("s", "S05"), ("g", changed) }, async client =>
        {
            await client.ReadRegisterAsync(1);
            await client.StepInstructionAsync();
            Require(Convert.ToHexString(await client.ReadRegisterAsync(1)) == "AAAAAAAA", "bulk reads must observe the current stop");
        });
        foreach (string malformed in new[] { "", "00", packet[..^2], packet + "00", "z" + packet[1..] })
            await WithPeerAsync(new[] { ("p1", ""), ("g", malformed) }, async client =>
                await ThrowsAsync<InvalidDataException>(() => client.ReadRegisterAsync(1)));
        await WithPeerAsync(new[] { ("p1", ""), ("g", "E00") }, async client =>
            await ThrowsAsync<RspRemoteException>(() => client.ReadRegisterAsync(1)));
        await WithPeerAsync(new[] { ("p1", "E00") }, async client =>
            await ThrowsAsync<RspRemoteException>(() => client.ReadRegisterAsync(1)));
        foreach (string malformed in new[] { "123", "zzzzzzzz" })
            await WithPeerAsync(new[] { ("p1", malformed) }, async client =>
                await ThrowsAsync<InvalidDataException>(() => client.ReadRegisterAsync(1)));
        await WithPeerAsync(new[] { ("p1", "81700000") }, async client =>
            Require(Convert.ToHexString(await client.ReadRegisterAsync(1)) == "81700000", "single-register targets remain supported"));
        await WithPeerAsync(new[] { ("pff", "") }, async client =>
            await ThrowsAsync<NotSupportedException>(() => client.ReadRegisterAsync(255)));
        // An unsupported optional telemetry write must not poison register reads.
        await WithPeerAsync(new[] { ("M80002000,4:00000001", ""), ("p1", ""), ("g", packet) }, async client =>
        {
            await ThrowsAsync<RspRemoteException>(() => client.WriteMemoryAsync(0x80002000, new byte[] { 0, 0, 0, 1 }));
            Require((await client.ReadRegisterAsync(1)).SequenceEqual(bytes.AsSpan(4, 4).ToArray()), "read after unsupported telemetry");
        });
        Console.WriteLine("libogc2 bulk-register compatibility checks passed.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task WithPeerAsync(IEnumerable<(string Command, string Reply)> exchanges, Func<RspClient, Task> operation)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var server = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync(deadline.Token);
                using var stream = peer.GetStream();
                foreach (var (command, reply) in exchanges)
                {
                    Require(await ReadFrameAsync(stream, deadline.Token) == command, "unexpected fallback command");
                    byte[] body = Encoding.ASCII.GetBytes(reply);
                    string frame = "+$" + reply + "#" + (body.Sum(b => (int)b) & 255).ToString("x2");
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(frame), deadline.Token);
                    Require(await ReadByteAsync(stream, deadline.Token) == '+', "reply checksum acknowledgement");
                }
            }, deadline.Token);
            await using var client = await RspClient.ConnectTcpAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, TimeSpan.FromSeconds(3), deadline.Token);
            await operation(client).WaitAsync(deadline.Token);
            await server.WaitAsync(deadline.Token);
        }
        finally { deadline.Cancel(); listener.Stop(); }
    }
    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken token)
    {
        byte[] buffer = new byte[1];
        if (await stream.ReadAsync(buffer, token) != 1) throw new EndOfStreamException();
        return buffer[0];
    }
    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        int ch;
        do { ch = await ReadByteAsync(stream, token); } while (ch == '+');
        Require(ch == '$', "command packet start");
        var data = new List<byte>();
        while ((ch = await ReadByteAsync(stream, token)) != '#') data.Add((byte)ch);
        int expected = Convert.ToInt32(new string(new[] { (char)await ReadByteAsync(stream, token), (char)await ReadByteAsync(stream, token) }), 16);
        Require((data.Sum(b => (int)b) & 255) == expected, "command checksum");
        return Encoding.ASCII.GetString(data.ToArray());
    }
}
