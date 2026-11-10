using System.Buffers.Binary;

namespace AgentDesktop.Core.Systems;

/// <summary>Image bytes with their format, pixel size, and display scale factor.</summary>
public sealed record ImageBuffer(byte[] Data, ImageFormat Format, uint Width, uint Height, double ScaleFactor)
{
    /// <summary>The largest PNG payload that the dimension parser accepts.</summary>
    public const int MaxPngInputBytes = 64 * 1024 * 1024;

    private const ulong MaxPngPixels = 64UL * 1024UL * 1024UL;

    private const int PngHeaderBytes = 33;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads width and height from a PNG IHDR chunk. Returns null when the header is malformed or unsupported.</summary>
    public static (uint Width, uint Height)? ParsePngDimensions(ReadOnlySpan<byte> data)
    {
        if (data.Length < PngHeaderBytes || data.Length > MaxPngInputBytes)
        {
            return null;
        }

        if (!data[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return null;
        }

        if (ReadBigEndianU32(data, 8) != 13 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        uint width = ReadBigEndianU32(data, 16);
        uint height = ReadBigEndianU32(data, 20);
        byte depth = data[24];
        byte color = data[25];

        return IsValidPngHeader(width, height, depth, color, data.Slice(26, 3))
            ? (width, height)
            : null;
    }

    private static uint ReadBigEndianU32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private static bool IsValidPngHeader(uint width, uint height, byte depth, byte color, ReadOnlySpan<byte> tail)
    {
        bool validDepth = color switch
        {
            0 => depth is 1 or 2 or 4 or 8 or 16,
            2 or 4 or 6 => depth is 8 or 16,
            3 => depth is 1 or 2 or 4 or 8,
            _ => false,
        };

        ulong pixels = (ulong)width * height;

        return width > 0
            && height > 0
            && pixels <= MaxPngPixels
            && validDepth
            && tail.Length == 3
            && tail[0] == 0
            && tail[1] == 0
            && tail[2] <= 1;
    }
}
