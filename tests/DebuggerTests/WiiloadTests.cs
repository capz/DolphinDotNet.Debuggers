using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DolphinDotNet.Debugger;

static class WiiloadTests
{
    public static async Task RunAsync()
    {
        foreach (var image in new[] { new byte[] { 0xff }, new byte[200000], RandomNumberGenerator.GetBytes(180000) })
        {
            var upload = WiiloadUpload.Prepare(image, Path.Combine("directory with spaces", "Game 日本.elf"), new[] { "--name", "two words" });
            using var output = new RecordingStream();
            await upload.WriteAsync(output);
            var frame = output.ToArray();
            Check(frame.AsSpan(0, 6).SequenceEqual(new byte[] { 72, 65, 88, 88, 0, 5 }), "magic/version");
            var argsSize = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(6));
            var compressedSize = BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(8));
            Check(BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(12)) == image.Length, "big-endian image length");
            Check(compressedSize == upload.CompressedSize && frame.Length == 16 + compressedSize + argsSize, "frame lengths");
            using var compressed = new MemoryStream(frame, 16, (int)compressedSize);
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            using var original = new MemoryStream();
            await zlib.CopyToAsync(original);
            Check(original.ToArray().SequenceEqual(image), "Swiss zlib inflate round trip, including expanding payloads");
            Check(Encoding.UTF8.GetString(frame.AsSpan(16 + (int)compressedSize)) == "Game 日本.elf\0--name\0two words\0\0", "UTF-8 argv and final empty string");
            Check(output.LargestWrite <= 3968 && output.Flushed, "serial chunking and flush");
            Check(upload.ImageSha256 == Convert.ToHexString(SHA256.HashData(image)), "exact uploaded identity");
        }
        var small = WiiloadUpload.Prepare(new byte[] { 1 }, "a");
        using (var output = new RecordingStream())
        {
            await small.WriteAsync(output);
            Check(output.ToArray().AsSpan(^3).SequenceEqual("a\0\0"u8), "default basename argv[0]");
            Check(output.CanWrite, "caller owns stream");
        }
        Reject<ArgumentException>(() => WiiloadUpload.Prepare(Array.Empty<byte>(), "Game.elf"));
        Reject<ArgumentException>(() => WiiloadUpload.Prepare(new byte[WiiloadUpload.MaximumImageSize + 1], "Game.elf"));
        Reject<ArgumentException>(() => WiiloadUpload.Prepare(new byte[] { 1 }, "Game.elf", new[] { "bad\0argument" }));
        Reject<ArgumentException>(() => WiiloadUpload.Prepare(new byte[] { 1 }, "a", new[] { new string('é', 511) }));
        // 2 bytes for argv[0], 1021 for the argument+NUL, 1 final NUL = 1024.
        WiiloadUpload.Prepare(new byte[] { 1 }, "a", new[] { new string('x', 1020) });
        Reject<ArgumentException>(() => WiiloadUpload.Prepare(new byte[] { 1 }, "a", new[] { new string('x', 1021) }));
        using (var cancelled = new CancellationTokenSource())
        using (var output = new RecordingStream())
        {
            cancelled.Cancel();
            await RejectAsync<OperationCanceledException>(() => small.WriteAsync(output, cancelled.Token));
            Check(output.Length == 0, "pre-cancellation writes nothing");
        }
        using (var output = new RecordingStream { FailAfter = 16 })
        {
            await RejectAsync<IOException>(() => small.WriteAsync(output));
            Check(output.Length == 16 && !output.Flushed, "payload failure prevents arguments and flush");
        }
        using (var output = new UnresponsiveStream())
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            await RejectAsync<OperationCanceledException>(() => small.WriteAsync(output, timeout.Token).WaitAsync(TimeSpan.FromSeconds(3)));
        var path = Path.GetTempFileName();
        try
        {
            Reject<ArgumentException>(() => WiiloadUpload.FromFile(path));
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            Check(WiiloadUpload.FromFile(path).ImageSize == 3, "file upload snapshot");
        }
        finally { File.Delete(path); }
        await RejectAsync<ArgumentException>(() => small.SendUsbGeckoAsync("", 115200, TimeSpan.FromSeconds(1)));
        await RejectAsync<ArgumentOutOfRangeException>(() => small.SendUsbGeckoAsync("COM1", 0, TimeSpan.FromSeconds(1)));
        Console.WriteLine("Swiss wiiload framing, zlib, arguments, limits, cancellation and I/O failure tests passed.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class RecordingStream : MemoryStream
    {
        public int LargestWrite;
        public bool Flushed;
        public int FailAfter = int.MaxValue;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            if (Length >= FailAfter) throw new IOException("Disconnected fixture");
            LargestWrite = Math.Max(LargestWrite, buffer.Length);
            return base.WriteAsync(buffer, token);
        }
        public override Task FlushAsync(CancellationToken token) { Flushed = true; return Task.CompletedTask; }
    }
    private sealed class UnresponsiveStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
            => new(new TaskCompletionSource().Task); // Simulate a driver that ignores cancellation.
    }
}
