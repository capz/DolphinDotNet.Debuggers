using System.Buffers.Binary;
using System.IO.Ports;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.IO.Compression;

namespace DolphinDotNet.Debugger;

public sealed record LauncherEndpoint(string Transport, string? Host, int Port, string? SerialPort, int BaudRate)
{
    public void Validate()
    {
        if (Transport == "tcp")
        {
            if (string.IsNullOrWhiteSpace(Host) || Port is < 1 or > 65535) throw new ArgumentException("Launcher needs a host and upload port between 1 and 65535.");
        }
        else if (Transport == "usbGecko")
        {
            if (string.IsNullOrWhiteSpace(SerialPort) || BaudRate <= 0) throw new ArgumentException("Launcher needs a serial device and positive baud rate.");
        }
        else throw new ArgumentException("Launcher transport must be tcp or usbGecko.");
    }
}

/// <summary>Version 1 of the DolphinDotNet console launcher protocol, independent of wiiload.</summary>
public static class LauncherProtocol
{
    public const uint MaximumImageBytes = 16 * 1024 * 1024 - 4096;
    public const ushort Hello = 1, Ready = 2, Upload = 3, Accepted = 4, Execute = 5, Executing = 6, Error = 7, Cancel = 8, Commit = 9, UploadZlib = 10;
    public const uint CompressionFlag = 0x80000000;

    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & (0u - (crc & 1)));
        }
        return ~crc;
    }
    private static async Task SendAsync(Stream stream, ushort kind, uint value, uint detail, CancellationToken token)
    {
        var header = new byte[16];
        "DNDL"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), kind);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), value);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(12), detail);
        await stream.WriteAsync(header, token).AsTask().WaitAsync(token).ConfigureAwait(false);
        await stream.FlushAsync(token).WaitAsync(token).ConfigureAwait(false);
    }
    private static async Task<(uint Value, uint Detail)> ReceiveAsync(Stream stream, ushort expected, CancellationToken token)
    {
        var header = new byte[16];
        await stream.ReadExactlyAsync(header, token).AsTask().WaitAsync(token).ConfigureAwait(false);
        if (!header.AsSpan(0, 4).SequenceEqual("DNDL"u8) || BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)) != 1)
            throw new InvalidDataException("This endpoint is not a compatible DolphinDotNet launcher (DNDL v1). Check the upload port and boot our launcher.");
        var kind = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6));
        var value = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        var detail = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
        if (kind == Error)
            throw new InvalidDataException("The console launcher rejected the request: " + (value switch {
                1 => "invalid request/state", 2 => "ELF exceeds the receiver's limit", 3 => "CRC32 mismatch",
                4 => "unsupported/malformed ELF or staging-memory overlap", 5 => "staging memory unavailable", 6 => "invalid compressed image", _ => $"error {value}" }));
        if (kind != expected) throw new InvalidDataException($"Unexpected launcher reply {kind}; expected {expected}.");
        return (value, detail);
    }
    public static async Task<(uint MaximumImageBytes, bool SupportsCompression)> HandshakeCapabilitiesAsync(Stream stream, uint nonce, CancellationToken token = default)
    {
        await SendAsync(stream, Hello, nonce, 0, token).ConfigureAwait(false);
        var reply = await ReceiveAsync(stream, Ready, token).ConfigureAwait(false);
        var limit = reply.Detail & ~CompressionFlag;
        if (reply.Value != nonce || limit is < 52 or > MaximumImageBytes)
            throw new InvalidDataException("Invalid/stale launcher readiness reply.");
        return (limit, (reply.Detail & CompressionFlag) != 0);
    }
    public static async Task<uint> HandshakeAsync(Stream stream, uint nonce, CancellationToken token = default)
        => (await HandshakeCapabilitiesAsync(stream, nonce, token).ConfigureAwait(false)).MaximumImageBytes;
    public static Task CancelAsync(Stream stream, uint nonce, CancellationToken token = default)
        => SendAsync(stream, Cancel, nonce, 0, token);

    public static async Task UploadAndExecuteAsync(Stream stream, uint nonce, uint maximumImageBytes,
        ReadOnlyMemory<byte> image, Action<string>? status = null, CancellationToken token = default,
        Func<int, int, Task>? progress = null, bool supportsCompression = false)
    {
        if (image.Length < 52 || image.Length > maximumImageBytes || image.Length > MaximumImageBytes)
            throw new ArgumentException("ELF does not fit the ready receiver's advertised image limit.", nameof(image));
        var crc = Crc32(image.Span);
        ReadOnlyMemory<byte> payload = image;
        if (supportsCompression)
        {
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(image.Span);
            if (compressed.Length + 4 < image.Length)
                payload = compressed.ToArray();
        }
        var useCompression = payload.Length != image.Length;
        await SendAsync(stream, useCompression ? UploadZlib : Upload, (uint)image.Length, crc, token).ConfigureAwait(false);
        if (useCompression)
        {
            var size = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(size, checked((uint)payload.Length));
            await stream.WriteAsync(size, token).AsTask().WaitAsync(token).ConfigureAwait(false);
        }
        if (progress is not null) await progress(0, payload.Length).ConfigureAwait(false);
        var lastStep = 0;
        for (var offset = 0; offset < payload.Length; offset += 3968)
        {
            var count = Math.Min(3968, payload.Length - offset);
            await stream.WriteAsync(payload.Slice(offset, count), token).AsTask().WaitAsync(token).ConfigureAwait(false);
            var sent = offset + count;
            var step = (int)((long)sent * 20 / payload.Length);
            if (progress is not null && sent < payload.Length && step > lastStep)
            {
                await progress(sent, payload.Length).ConfigureAwait(false);
                lastStep = step;
            }
        }
        await stream.FlushAsync(token).WaitAsync(token).ConfigureAwait(false);
        if (progress is not null) await progress(payload.Length, payload.Length).ConfigureAwait(false);
        var accepted = await ReceiveAsync(stream, Accepted, token).ConfigureAwait(false);
        if (accepted.Value != image.Length || accepted.Detail != crc)
            throw new InvalidDataException("The receiver acknowledged a different ELF. Execution was not requested.");
        status?.Invoke("Console validated the ELF. Requesting execution.");
        await SendAsync(stream, Execute, nonce, 0, token).ConfigureAwait(false);
        var executing = await ReceiveAsync(stream, Executing, token).ConfigureAwait(false);
        if (executing.Value != nonce || executing.Detail != 0)
            throw new InvalidDataException("Invalid execution acknowledgement. Console state is unknown; return to the launcher before retrying.");
        // The native receiver must know this ACK reached the host before shutting down the NIC.
        await SendAsync(stream, Commit, nonce, 0, token).ConfigureAwait(false);
    }
}

/// <summary>Owns one ready receiver session; never retries an upload/execute request.</summary>
public sealed class LauncherClient : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly IDisposable _owner;
    private readonly uint _nonce;
    private bool _committed, _disposed;
    public uint MaximumImageBytes { get; private set; }
    public bool SupportsCompression { get; private set; }

    private LauncherClient(Stream stream, IDisposable owner)
    {
        _stream = stream; _owner = owner;
        _nonce = BinaryPrimitives.ReadUInt32BigEndian(RandomNumberGenerator.GetBytes(4));
    }
    public static async Task<LauncherClient> WaitForReadyAsync(LauncherEndpoint endpoint, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        endpoint.Validate();
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        Exception? last = null;
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                LauncherClient? client = null;
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                attempt.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    if (endpoint.Transport == "tcp")
                    {
                        var tcp = new TcpClient();
                        try { await tcp.ConnectAsync(endpoint.Host!, endpoint.Port, attempt.Token).ConfigureAwait(false); }
                        catch { tcp.Dispose(); throw; }
                        tcp.NoDelay = true;
                        client = new LauncherClient(tcp.GetStream(), tcp);
                    }
                    else
                    {
                        var port = new SerialPort(endpoint.SerialPort!, endpoint.BaudRate, Parity.None, 8, StopBits.One)
                        { Handshake = Handshake.RequestToSend, ReadTimeout = 2000, WriteTimeout = 2000 };
                        try { port.Open(); port.DiscardInBuffer(); port.DiscardOutBuffer(); }
                        catch { port.Dispose(); throw; }
                        client = new LauncherClient(port.BaseStream, port);
                    }
                    (client.MaximumImageBytes, client.SupportsCompression) = await LauncherProtocol.HandshakeCapabilitiesAsync(client._stream, client._nonce, attempt.Token).ConfigureAwait(false);
                    return client;
                }
                catch (InvalidDataException error)
                {
                    client?.Close();
                    // A stale USB Gecko packet from the previous GDB session can
                    // provoke a DNDL rejection before the new readiness handshake.
                    // Only readiness is retried; upload/execution is never retried.
                    if (endpoint.Transport != "usbGecko") throw;
                    last = error;
                }
                catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or UnauthorizedAccessException)
                {
                    last = error;
                    client?.Close();
                }
                await Task.Delay(250, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Console is not ready to receive an ELF. Boot DolphinDotNet.Launcher and check the upload endpoint. {last?.Message}", last);
        }
    }
    public async Task UploadAndExecuteAsync(ReadOnlyMemory<byte> image, TimeSpan timeout,
        Action<string>? status = null, CancellationToken cancellationToken = default,
        Func<int, int, Task>? progress = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed) throw new InvalidOperationException("This launcher session has already been used.");
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _committed = true; // A partial upload must never be retried on this session.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            if (_owner is SerialPort serial)
                serial.ReadTimeout = serial.WriteTimeout = (int)Math.Ceiling(timeout.TotalMilliseconds);
            await LauncherProtocol.UploadAndExecuteAsync(_stream, _nonce, MaximumImageBytes, image, status, deadline.Token, progress, SupportsCompression).ConfigureAwait(false);
            // The final COMMIT has no reply. Keep the upload port alive until the
            // driver's output queue drains before reopening the same device for GDB.
            if (_owner is SerialPort uploadPort)
                while (uploadPort.BytesToWrite != 0)
                    await Task.Delay(10, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Launcher transfer/execution acknowledgement timed out. Return to the launcher before retrying."); }
    }
    private void Close()
    {
        if (_disposed) return;
        _disposed = true;
        _owner.Dispose();
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        try
        {
            if (!_committed)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await LauncherProtocol.CancelAsync(_stream, _nonce, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally { Close(); }
    }
}
