namespace DolphinDotNet.Debugger;

/// <summary>Classic PowerPC32 RSP register order used by libogc2's libdb stub.</summary>
internal static class PowerPcRegisterPacket
{
    private const int GprBytes = 32 * 4;
    private const int FprBytes = 32 * 8;
    private const int PacketBytes = GprBytes + FprBytes + 7 * 4;

    internal static (int Offset, int Size) Location(int index) => index switch
    {
        >= 0 and < 32 => (index * 4, 4),
        >= 32 and < 64 => (GprBytes + (index - 32) * 8, 8),
        >= 64 and <= 70 => (GprBytes + FprBytes + (index - 64) * 4, 4),
        _ => throw new NotSupportedException($"Register {index} is absent from the classic PowerPC bulk register packet.")
    };

    internal static byte[] Extract(string response, int offset, int size)
    {
        if (response.Length != PacketBytes * 2)
            throw new InvalidDataException($"Invalid PowerPC bulk register packet: expected {PacketBytes} bytes, received {response.Length} hex characters.");
        byte[] bytes;
        try { bytes = Convert.FromHexString(response); }
        catch (FormatException ex) { throw new InvalidDataException("Invalid PowerPC bulk register bytes.", ex); }
        // Return the raw target byte order; callers decode integers/floats.
        return bytes.AsSpan(offset, size).ToArray();
    }
}
