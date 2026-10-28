using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using DolphinDotNet.Debugger;

static class LauncherTests
{
    private const uint Nonce = 0x12345678;
    public static async Task RunAsync()
    {
        Check(LauncherProtocol.Crc32("123456789"u8) == 0xcbf43926, "standard CRC32 vector");
        var image = Enumerable.Range(0, 10000).Select(i => (byte)i).ToArray();
        var crc = LauncherProtocol.Crc32(image);
        using (var stream = new Duplex(Reply(LauncherProtocol.Ready, Nonce, 16000), Reply(LauncherProtocol.Accepted, (uint)image.Length, crc), Reply(LauncherProtocol.Executing, Nonce, 0)))
        {
            var limit = await LauncherProtocol.HandshakeAsync(stream, Nonce);
            Check(limit == 16000, "advertised receiver capacity");
            var progress = new List<int>();
            await LauncherProtocol.UploadAndExecuteAsync(stream, Nonce, limit, image,
                progress: (sent, total) =>
                {
                    Check(total == image.Length && sent <= total, "bounded progress uses the current image size");
                    Check(progress.Count == 0 ? sent == 0 : sent > progress[^1], "monotonic progress starts at zero");
                    Check(stream.Output.Length >= 32 + sent, "progress never precedes completed writes");
                    if (sent == total) Check(stream.Flushed, "completion follows the payload flush");
                    if (sent == 0) stream.Flushed = false;
                    progress.Add(sent);
                    return Task.CompletedTask;
                });
            Check(progress[^1] == image.Length && progress.Count is > 2 and <= 22, "bounded notifications end at the exact byte count");
            var sent = stream.Output.ToArray();
            Check(sent.AsSpan(0, 16).SequenceEqual(Reply(LauncherProtocol.Hello, Nonce, 0)), "exact hello frame");
            Check(sent.AsSpan(16, 16).SequenceEqual(Reply(LauncherProtocol.Upload, (uint)image.Length, crc)), "upload header");
            Check(sent.AsSpan(32, image.Length).SequenceEqual(image), "complete uncompressed ELF");
            Check(sent.AsSpan(32 + image.Length, 16).SequenceEqual(Reply(LauncherProtocol.Execute, Nonce, 0)), "execute after validated acknowledgement");
            Check(sent.AsSpan(48 + image.Length).SequenceEqual(Reply(LauncherProtocol.Commit, Nonce, 0)), "commit after execution ACK reaches host");
            Check(stream.LargestWrite <= 3968, "serial FIFO chunks");
        }
        foreach (var ready in new[] { Reply(LauncherProtocol.Ready, Nonce + 1, 1000), Reply(LauncherProtocol.Ready, Nonce, 1), Reply(LauncherProtocol.Accepted, Nonce, 1000), Reply(LauncherProtocol.Ready, Nonce, uint.MaxValue) })
        {
            using var stream = new Duplex(ready);
            await RejectAsync<InvalidDataException>(() => LauncherProtocol.HandshakeAsync(stream, Nonce));
            Check(stream.Output.Length == 16, "bad readiness sends no payload");
        }
        using (var stream = new Duplex(Reply(LauncherProtocol.Accepted, 9999, crc)))
        {
            await RejectAsync<InvalidDataException>(() => LauncherProtocol.UploadAndExecuteAsync(stream, Nonce, 16000, image));
            Check(stream.Output.Length == 16 + image.Length, "mismatched acknowledgement cannot execute");
        }
        using (var stream = new Duplex(Reply(LauncherProtocol.Error, 4, 0)))
        {
            await RejectAsync<InvalidDataException>(() => LauncherProtocol.UploadAndExecuteAsync(stream, Nonce, 16000, image));
            Check(stream.Output.Length == 16 + image.Length, "rejected ELF cannot execute");
        }
        using (var stream = new Duplex(Reply(LauncherProtocol.Accepted, (uint)image.Length, crc), Reply(LauncherProtocol.Executing, Nonce + 1, 0)))
        {
            await RejectAsync<InvalidDataException>(() => LauncherProtocol.UploadAndExecuteAsync(stream, Nonce, 16000, image));
            Check(stream.Output.Length == 32 + image.Length, "invalid execution ACK cannot send final COMMIT");
        }
        using (var stream = new Duplex())
        {
            await RejectAsync<ArgumentException>(() => LauncherProtocol.UploadAndExecuteAsync(stream, Nonce, 60, image));
            Check(stream.Output.Length == 0, "capacity checked before upload");
            await LauncherProtocol.CancelAsync(stream, Nonce);
            Check(stream.Output.ToArray().SequenceEqual(Reply(LauncherProtocol.Cancel, Nonce, 0)), "probe cancel");
        }
        using (var stream = new Duplex { FailAfterBytes = 16 + 3968 })
        {
            var progress = new List<int>();
            await RejectAsync<IOException>(() => LauncherProtocol.UploadAndExecuteAsync(stream, Nonce, 16000, image,
                progress: (sent, total) => { progress.Add(sent); return Task.CompletedTask; }));
            Check(progress.Count > 1 && progress[^1] < image.Length, "interrupted writes never report completion");
            Check(stream.Output.Length == 16 + 3968, "interrupted upload sends no execute or commit");
        }
        using (var stream = new Duplex(Reply(LauncherProtocol.Ready, Nonce, 16000)))
        using (var timeout = new CancellationTokenSource())
        {
            timeout.Cancel();
            await RejectAsync<OperationCanceledException>(() => LauncherProtocol.HandshakeAsync(stream, Nonce, timeout.Token));
            Check(stream.Output.Length == 0, "cancelled probe sends nothing");
        }
        await ProbeOwnsAndReleasesConnectionAsync();
        Console.WriteLine("Native launcher readiness, CRC32, short I/O, execution acknowledgements and probe cancellation passed.");
    }
    private static async Task ProbeOwnsAndReleasesConnectionAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var server = Task.Run(async () =>
            {
                using var peer = await listener.AcceptTcpClientAsync();
                var stream = peer.GetStream(); var hello = new byte[16];
                await stream.ReadExactlyAsync(hello);
                var nonce = BinaryPrimitives.ReadUInt32BigEndian(hello.AsSpan(8));
                await stream.WriteAsync(Reply(LauncherProtocol.Ready, nonce, 16000));
                var cancel = new byte[16]; await stream.ReadExactlyAsync(cancel);
                Check(cancel.SequenceEqual(Reply(LauncherProtocol.Cancel, nonce, 0)), "readiness probe releases native session with CANCEL");
                Check(stream.ReadByte() == -1, "probe closes transport");
            });
            var endpoint = new LauncherEndpoint("tcp", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, null, 0);
            await using (var client = await LauncherClient.WaitForReadyAsync(endpoint, TimeSpan.FromSeconds(3)))
                Check(client.MaximumImageBytes == 16000, "connected readiness");
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { listener.Stop(); }
    }
    private static byte[] Reply(ushort kind, uint value, uint detail)
    {
        var b = new byte[16]; "DNDL"u8.CopyTo(b);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(4), 1); BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(6), kind);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8), value); BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(12), detail);
        return b;
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static async Task RejectAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class Duplex(params byte[][] responses) : Stream
    {
        private readonly MemoryStream input = new(responses.SelectMany(r => r).ToArray());
        public readonly MemoryStream Output = new();
        public int LargestWrite;
        public int FailAfterBytes = int.MaxValue;
        public bool Flushed;
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, Math.Min(3, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { token.ThrowIfCancellationRequested();return input.ReadAsync(buffer[..Math.Min(3,buffer.Length)],token); }
        public override void Write(byte[] buffer, int offset, int count) { LargestWrite=Math.Max(LargestWrite,count);Output.Write(buffer,offset,count); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Output.Length + buffer.Length > FailAfterBytes) throw new IOException("Interrupted upload fixture");
            LargestWrite=Math.Max(LargestWrite,buffer.Length);
            return Output.WriteAsync(buffer,token);
        }
        public override void Flush() { Flushed = true; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { input.Dispose();Output.Dispose(); }base.Dispose(disposing); }
    }
}
