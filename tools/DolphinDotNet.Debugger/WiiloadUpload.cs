using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Text;

namespace DolphinDotNet.Debugger;

/// <summary>A prepared wiiload 0.5 upload, usable by IDEs without a wiiload executable.</summary>
public sealed class WiiloadUpload
{
    public const int MaximumImageSize = 32 * 1024 * 1024;
    public const int MaximumArgumentSize = 1024;
    private const int SerialBlockSize = 3968;
    private readonly byte[] _header, _payload, _arguments;
    public string ImageSha256 { get; }
    public int ImageSize { get; }
    public int CompressedSize => _payload.Length;

    private WiiloadUpload(byte[] image, byte[] arguments)
    {
        ImageSize = image.Length;
        ImageSha256 = Convert.ToHexString(SHA256.HashData(image));
        using var compressed = new MemoryStream();
        // Swiss always inflates the payload, even when compression increases its size.
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(image);
        _payload = compressed.ToArray();
        _arguments = arguments;
        _header = new byte[16];
        "HAXX"u8.CopyTo(_header);
        _header[5] = 5;
        BinaryPrimitives.WriteUInt16BigEndian(_header.AsSpan(6), checked((ushort)arguments.Length));
        BinaryPrimitives.WriteUInt32BigEndian(_header.AsSpan(8), checked((uint)_payload.Length));
        BinaryPrimitives.WriteUInt32BigEndian(_header.AsSpan(12), checked((uint)image.Length));
    }

    /// <summary>Encode argv[0] as the basename, then UTF-8 arguments and a final empty string.</summary>
    public static WiiloadUpload Prepare(byte[] image, string filename, IReadOnlyList<string>? arguments = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Length is 0 or > MaximumImageSize)
            throw new ArgumentException("Upload image must contain between 1 byte and 32 MiB.", nameof(image));
        var basename = Path.GetFileName(filename);
        if (string.IsNullOrWhiteSpace(basename)) throw new ArgumentException("An upload filename is required.", nameof(filename));
        var utf8 = new UTF8Encoding(false, true);
        using var encoded = new MemoryStream();
        foreach (var arg in new[] { basename }.Concat(arguments ?? Array.Empty<string>()))
        {
            if (arg is null || arg.Contains('\0')) throw new ArgumentException("Upload arguments cannot contain NUL or null.", nameof(arguments));
            // Count before allocating; enforce the limit in bytes, including both terminators.
            if (encoded.Length + utf8.GetByteCount(arg) + 2 > MaximumArgumentSize)
                throw new ArgumentException("Upload arguments exceed 1024 UTF-8 bytes.", nameof(arguments));
            encoded.Write(utf8.GetBytes(arg));
            encoded.WriteByte(0);
        }
        encoded.WriteByte(0);
        return new WiiloadUpload(image, encoded.ToArray());
    }

    public static WiiloadUpload FromFile(string filename, IReadOnlyList<string>? arguments = null)
    {
        using var file = File.OpenRead(filename);
        if (file.Length is 0 or > MaximumImageSize)
            throw new ArgumentException("Upload image must contain between 1 byte and 32 MiB.", nameof(filename));
        var image = new byte[checked((int)file.Length)];
        file.ReadExactly(image);
        return Prepare(image, filename, arguments);
    }

    /// <summary>Write the complete frame. The caller owns the stream, including cleanup on cancellation.</summary>
    public async Task WriteAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        foreach (var section in new[] { _header, _payload, _arguments })
        for (var offset = 0; offset < section.Length; offset += SerialBlockSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await stream.WriteAsync(section.AsMemory(offset, Math.Min(SerialBlockSize, section.Length - offset)), cancellationToken)
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        await stream.FlushAsync(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Open, upload and drain the VCP; always release it before an RSP connection.</summary>
    public async Task SendUsbGeckoAsync(string portName, int baudRate, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("Serial port is required.", nameof(portName));
        if (baudRate <= 0) throw new ArgumentOutOfRangeException(nameof(baudRate));
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        deadline.Token.ThrowIfCancellationRequested();
        using var port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.RequestToSend,
            ReadTimeout = (int)timeout.TotalMilliseconds, WriteTimeout = (int)timeout.TotalMilliseconds
        };
        try
        {
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            await WriteAsync(port.BaseStream, deadline.Token).ConfigureAwait(false);
            // Flush alone does not guarantee that the driver's output queue has drained.
            while (port.BytesToWrite != 0)
                await Task.Delay(10, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"USB Gecko upload timed out after {timeout.TotalSeconds:g} seconds. Check Swiss, the serial device and its driver.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException)
        {
            throw new IOException($"USB Gecko upload on '{portName}' failed: {error.Message} Close other users of this port and boot Swiss before retrying.", error);
        }
    }
}
