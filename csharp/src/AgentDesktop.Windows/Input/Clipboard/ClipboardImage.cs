using System.Buffers.Binary;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>Pure clipboard image marshalling: registered PNG passthrough and CF_DIB/CF_DIBV5 to
/// PNG. Nothing here opens a clipboard. Every failure is INVALID_ARGS marked unsent: a rejected
/// payload leaves the clipboard untouched. No message carries pixel data, only budgets, sizes,
/// header fields, and offsets.</summary>
internal static class ClipboardImage
{
    /// <summary>Byte size of BITMAPINFOHEADER, the header every accepted DIB begins with.</summary>
    private const uint BitmapInfoHeaderSize = 40;

    /// <summary>Byte size of BITMAPV5HEADER. Its masks sit inside the header.</summary>
    private const uint BitmapV5HeaderSize = 124;

    /// <summary>BI_RGB: rows hold BGR bytes with no masks between header and pixels.</summary>
    private const uint BiRgb = 0;

    /// <summary>BI_BITFIELDS: a 12-byte mask section follows a BITMAPINFOHEADER. Only 24 and 32 bit counts are accepted.</summary>
    private const uint BiBitfields = 3;

    /// <summary>Decoded-image budget: refused before a pixel buffer is allocated.</summary>
    private const ulong MaxImagePixels = 64UL * 1024UL * 1024UL;

    /// <summary>Decodes a registered PNG clipboard payload at scale one.</summary>
    internal static ImageBuffer DecodePngClipboard(byte[] png)
    {
        var (width, height) = ValidatePngPayload(png, argument: false);
        return new ImageBuffer(png, ImageFormat.Png, width, height, 1.0);
    }

    /// <summary>Decodes a CF_DIB or CF_DIBV5 payload to PNG, under the caller's deadline.</summary>
    internal static ImageBuffer DecodeDibClipboard(byte[] dib, Deadline deadline)
    {
        var (bgra, width, height) = DibToBgra(dib);
        var stride = CheckedU32(
            CheckedMultiply(width, 4, "DIB width overflows the BGRA stride budget"),
            "DIB width overflows the BGRA stride budget");
        var png = EncodeBgraToPng(bgra, width, height, stride, deadline);
        return new ImageBuffer(png, ImageFormat.Png, width, height, 1.0);
    }

    /// <summary>Validates bytes for a PNG clipboard write without copying them.</summary>
    internal static (byte[] Bytes, uint Width, uint Height) PrepareClipboardPng(byte[] bytes)
    {
        var (width, height) = ValidatePngPayload(bytes, argument: true);
        return (bytes, width, height);
    }

    /// <summary>Returns the bytes to place on the clipboard for an image.</summary>
    internal static byte[] PngBytesForClipboard(ImageBuffer image)
    {
        var prepared = PrepareClipboardPng(image.Data);
        if (image.Format != ImageFormat.Png
            || prepared.Width != image.Width
            || prepared.Height != image.Height
            || !double.IsFinite(image.ScaleFactor)
            || image.ScaleFactor <= 0.0)
        {
            throw ArgumentError("Clipboard image metadata does not match its PNG payload");
        }

        return prepared.Bytes;
    }

    /// <summary>Encodes a PNG payload as CF_DIB for a clipboard write, as a bottom-up 32-bit
    /// BITMAPINFOHEADER DIB.</summary>
    internal static byte[] EncodeDibFromPng(byte[] png, Deadline deadline)
    {
        ValidatePngPayload(png, argument: true);
        var (bgra, width, height) = DecodePngToBgra(png, deadline);
        return BgraToDib(bgra, width, height);
    }

    /// <summary>Expands a CF_DIB or CF_DIBV5 payload to BGRA, checking every size before a pixel is read.</summary>
    private static (byte[] Bgra, uint Width, uint Height) DibToBgra(byte[] dib)
    {
        if (dib.Length < (int)BitmapInfoHeaderSize)
        {
            throw PayloadError("DIB payload is shorter than BITMAPINFOHEADER");
        }

        var headerSize = ReadU32(dib, 0, "DIB header size");
        if (headerSize != BitmapInfoHeaderSize && headerSize != BitmapV5HeaderSize)
        {
            throw PayloadError("DIB header size is not BITMAPINFOHEADER or BITMAPV5HEADER");
        }

        if (dib.Length < (int)headerSize)
        {
            throw PayloadError("DIB payload is shorter than its declared header");
        }

        var widthI = ReadI32(dib, 4, "DIB biWidth");
        var heightI = ReadI32(dib, 8, "DIB biHeight");
        var planes = ReadU16(dib, 12, "DIB biPlanes");
        var bitCount = ReadU16(dib, 14, "DIB biBitCount");
        var compression = ReadU32(dib, 16, "DIB biCompression");
        var clrUsed = ReadU32(dib, 32, "DIB biClrUsed");
        if (planes != 1)
        {
            throw PayloadError("DIB biPlanes must be 1");
        }

        if (bitCount != 24 && bitCount != 32)
        {
            throw PayloadError("DIB biBitCount must be 24 or 32");
        }

        if (compression != BiRgb && compression != BiBitfields)
        {
            throw PayloadError("DIB compression must be BI_RGB or BI_BITFIELDS");
        }

        if (widthI <= 0)
        {
            throw PayloadError("DIB biWidth must be positive");
        }

        if (heightI == 0)
        {
            throw PayloadError("DIB biHeight must be non-zero");
        }

        var width = (uint)widthI;
        var topDown = heightI < 0;
        var height = (uint)Math.Abs((long)heightI);
        ValidateDimensions(width, height, argument: false);

        var pixelOffset = PixelArrayOffset(headerSize, compression, bitCount, clrUsed);
        var stride = DibStrideBytes(width, bitCount);
        var pixelBytes = CheckedLength(
            CheckedMultiply(stride, height, "DIB pixel buffer size overflowed"),
            "DIB pixel buffer size overflowed");
        var needed = CheckedAdd(pixelOffset, pixelBytes, "DIB pixel buffer size overflowed");
        if (needed > dib.Length)
        {
            throw PayloadError("DIB pixel array is truncated");
        }

        var bgra = ExpandDibPixels(dib, (pixelOffset, width, height, bitCount, stride, topDown));
        return (bgra, width, height);
    }

    /// <summary>Writes a BGRA buffer as a bottom-up 32-bit BITMAPINFOHEADER DIB.</summary>
    private static byte[] BgraToDib(byte[] bgra, uint width, uint height)
    {
        ValidateDimensions(width, height, argument: true);
        var stride = DibStrideBytes(width, 32);
        var rowBytes = CheckedLength(CheckedMultiply(width, 4, "BGRA width overflows"), "BGRA width overflows");
        var needed = CheckedLength(CheckedMultiply(rowBytes, height, "BGRA buffer size overflows"), "BGRA buffer size overflows");
        if (bgra.Length < needed)
        {
            throw ArgumentError("BGRA buffer is shorter than width times height times four");
        }

        var total = CheckedLength(
            CheckedAdd(BitmapInfoHeaderSize, CheckedMultiply(stride, height, "DIB output size overflowed"), "DIB output size overflowed"),
            "DIB output size overflowed");
        var result = new byte[total];
        WriteBitmapInfoHeader(result, width, height, stride);
        for (uint y = 0; y < height; y++)
        {
            var srcY = height - 1 - y;
            var src = (long)srcY * rowBytes;
            var dst = (long)BitmapInfoHeaderSize + (long)y * stride;
            Array.Copy(bgra, src, result, dst, rowBytes);
        }

        return result;
    }

    /// <summary>Where the DIB pixel array begins: after the header, mask section, and palette.</summary>
    private static int PixelArrayOffset(uint headerSize, uint compression, ushort bitCount, uint clrUsed)
    {
        long offset = headerSize;
        if (compression == BiBitfields && headerSize == BitmapInfoHeaderSize)
        {
            offset = CheckedAdd(offset, 12, "DIB bitfields mask section overflowed");
        }

        long paletteEntries = bitCount <= 8
            ? (clrUsed == 0 ? 1L << bitCount : clrUsed)
            : (clrUsed > 0 ? clrUsed : 0u);
        var paletteBytes = CheckedMultiply(paletteEntries, 4, "DIB palette size overflowed");
        return CheckedLength(CheckedAdd(offset, paletteBytes, "DIB pixel offset overflowed"), "DIB pixel offset overflowed");
    }

    /// <summary>Row stride of a DIB: rows are word-aligned, so the bit width rounds up to whole
    /// 32-bit words. A stride that cannot fit in u32 is refused rather than narrowed.</summary>
    private static uint DibStrideBytes(uint width, ushort bitCount)
    {
        var bits = CheckedMultiply(width, bitCount, "DIB row bit width overflowed");
        var stride = CheckedMultiply((bits + 31) / 32, 4, "DIB row stride overflowed");
        return CheckedU32(stride, "DIB row stride does not fit in u32");
    }

    /// <summary>Expands DIB rows to top-down BGRA rows; 24-bit rows are fully opaque.</summary>
    private static byte[] ExpandDibPixels(
        byte[] dib,
        (int PixelOffset, uint Width, uint Height, ushort BitCount, uint Stride, bool TopDown) layout)
    {
        var outStride = CheckedLength(CheckedMultiply(layout.Width, 4, "BGRA output stride overflowed"), "BGRA output stride overflowed");
        var result = new byte[CheckedLength(CheckedMultiply(outStride, layout.Height, "BGRA output size overflowed"), "BGRA output size overflowed")];
        var srcBpp = layout.BitCount / 8;
        for (uint y = 0; y < layout.Height; y++)
        {
            var srcY = layout.TopDown ? y : layout.Height - 1 - y;
            long srcRow = layout.PixelOffset + (long)srcY * layout.Stride;
            var dstRow = (int)((long)y * outStride);
            for (uint x = 0; x < layout.Width; x++)
            {
                var src = srcRow + (long)x * srcBpp;
                var dst = dstRow + (int)x * 4;
                result[dst] = ReadPixel(dib, src);
                result[dst + 1] = ReadPixel(dib, src + 1);
                result[dst + 2] = ReadPixel(dib, src + 2);
                result[dst + 3] = layout.BitCount == 32 ? ReadPixel(dib, src + 3) : byte.MaxValue;
            }
        }

        return result;
    }

    private static void WriteBitmapInfoHeader(byte[] dst, uint width, uint height, uint stride)
    {
        WriteU32(dst, 0, BitmapInfoHeaderSize);
        WriteI32(dst, 4, (int)width);
        WriteI32(dst, 8, (int)height);
        WriteU16(dst, 12, 1);
        WriteU16(dst, 14, 32);
        WriteU32(dst, 16, BiRgb);
        WriteU32(dst, 20, SaturatingMultiply(stride, height));
        WriteI32(dst, 24, 0);
        WriteI32(dst, 28, 0);
        WriteU32(dst, 32, 0);
        WriteU32(dst, 36, 0);
    }

    private static uint SaturatingMultiply(uint stride, uint height)
    {
        var product = (ulong)stride * height;
        return product > uint.MaxValue ? uint.MaxValue : (uint)product;
    }

    private static byte ReadPixel(byte[] pixels, long index)
    {
        if (index < 0 || index >= pixels.Length)
        {
            throw PayloadError("DIB pixel row is truncated");
        }

        return pixels[(int)index];
    }

    /// <summary>Validates a PNG payload and returns the dimensions its header proves.</summary>
    private static (uint Width, uint Height) ValidatePngPayload(byte[] png, bool argument)
    {
        if (png.Length == 0)
        {
            throw Choose(argument, "Clipboard PNG payload is empty", "Clipboard PNG payload is empty");
        }

        var budgetMiB = PngInputBudgetBytes() / (1024 * 1024);
        if (png.Length > PngInputBudgetBytes())
        {
            throw Choose(
                argument,
                $"Image exceeds the {budgetMiB} MiB encoded-data budget",
                $"Clipboard image exceeds the {budgetMiB} MiB encoded-data budget");
        }

        var dimensions = ParsePngDimensions(png);
        ValidateDimensions(dimensions.Width, dimensions.Height, argument);
        return dimensions;
    }

    private static void ValidateDimensions(uint width, uint height, bool argument)
    {
        var pixels = (ulong)width * height;
        if (width > 0 && height > 0 && pixels <= MaxImagePixels)
        {
            return;
        }

        throw Choose(argument, "Image exceeds the decoded-image budget", "Clipboard image exceeds the decoded-image budget");
    }

    private static long CheckedMultiply(long left, long right, string message)
    {
        if (left > 0 && right > long.MaxValue / left)
        {
            throw PayloadError(message);
        }

        return left * right;
    }

    private static long CheckedAdd(long left, long right, string message)
    {
        if (right > long.MaxValue - left)
        {
            throw PayloadError(message);
        }

        return left + right;
    }

    private static int CheckedLength(long value, string message)
    {
        if (value > int.MaxValue)
        {
            throw PayloadError(message);
        }

        return (int)value;
    }

    private static uint CheckedU32(long value, string message)
    {
        if (value < 0 || value > uint.MaxValue)
        {
            throw PayloadError(message);
        }

        return (uint)value;
    }

    private static uint ReadU32(byte[] bytes, int offset, string field)
    {
        if (offset + 4 > bytes.Length)
        {
            throw PayloadError($"{field} is beyond the payload end");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static int ReadI32(byte[] bytes, int offset, string field)
    {
        if (offset + 4 > bytes.Length)
        {
            throw PayloadError($"{field} is beyond the payload end");
        }

        return BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
    }

    private static ushort ReadU16(byte[] bytes, int offset, string field)
    {
        if (offset + 2 > bytes.Length)
        {
            throw PayloadError($"{field} is beyond the payload end");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    }

    private static void WriteU32(byte[] dst, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(dst.AsSpan(offset, 4), value);

    private static void WriteI32(byte[] dst, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(dst.AsSpan(offset, 4), value);

    private static void WriteU16(byte[] dst, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(dst.AsSpan(offset, 2), value);

    private static AdapterError Choose(bool argument, string argumentMessage, string payloadMessage) =>
        argument ? ArgumentError(argumentMessage) : PayloadError(payloadMessage);

    private static AdapterError ArgumentError(string message) =>
        AdapterError.InvalidArgs(message).Unsent();

    private static AdapterError PayloadError(string message) =>
        AdapterError.InvalidArgs(message).Unsent();

    /// <summary>SEAM: MAX_PNG_INPUT_BYTES in core is not ported; no budget is invented.</summary>
    private static long PngInputBudgetBytes() => throw AdapterError.NotSupported("clipboard-image");

    /// <summary>SEAM: the shared PNG parser is not ported; no dimensions are invented.</summary>
    private static (uint Width, uint Height) ParsePngDimensions(byte[] png) =>
        throw AdapterError.NotSupported("clipboard-image");

    /// <summary>SEAM: the Windows PNG encoder is not ported.</summary>
    private static byte[] EncodeBgraToPng(byte[] bgra, uint width, uint height, uint stride, Deadline deadline) =>
        throw AdapterError.NotSupported("clipboard-image");

    /// <summary>SEAM: the Windows PNG decoder is not ported.</summary>
    private static (byte[] Bgra, uint Width, uint Height) DecodePngToBgra(byte[] png, Deadline deadline) =>
        throw AdapterError.NotSupported("clipboard-image");
}
