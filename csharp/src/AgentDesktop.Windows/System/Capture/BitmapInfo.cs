using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>
/// The native <c>BITMAPINFO</c>: a header followed by one colour-table entry. A 32-bit BI_RGB bitmap uses no
/// palette, so the entry stays zero. The layout is fixed by the ABI, so it carries every field the platform
/// declares, in the order the platform declares it.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfo
{
    internal BitmapInfo(BitmapInfoHeader header, uint colorTable)
    {
        Header = header;
        ColorTable = colorTable;
    }

    internal BitmapInfoHeader Header { get; }

    internal uint ColorTable { get; }
}

/// <summary>
/// The native <c>BITMAPINFOHEADER</c>, in the field order the platform declares. The layout is fixed by the
/// ABI, so it carries every field the struct defines.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BitmapInfoHeader
{
    internal BitmapInfoHeader(
        uint size,
        int width,
        int height,
        ushort planes,
        ushort bitCount,
        uint compression,
        uint sizeImage,
        int xPelsPerMeter,
        int yPelsPerMeter,
        uint colorsUsed,
        uint colorsImportant)
    {
        Size = size;
        Width = width;
        Height = height;
        Planes = planes;
        BitCount = bitCount;
        Compression = compression;
        SizeImage = sizeImage;
        XPelsPerMeter = xPelsPerMeter;
        YPelsPerMeter = yPelsPerMeter;
        ColorsUsed = colorsUsed;
        ColorsImportant = colorsImportant;
    }

    internal uint Size { get; }

    internal int Width { get; }

    internal int Height { get; }

    internal ushort Planes { get; }

    internal ushort BitCount { get; }

    internal uint Compression { get; }

    internal uint SizeImage { get; }

    internal int XPelsPerMeter { get; }

    internal int YPelsPerMeter { get; }

    internal uint ColorsUsed { get; }

    internal uint ColorsImportant { get; }
}
