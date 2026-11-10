using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>
/// BGRA to PNG codec. The Rust adapter encodes and decodes through WIC with an in-memory stream. This port
/// validates input and output budgets exactly as the Rust codec does; the WIC calls are a seam until a WIC
/// binding exists in the port.
/// </summary>
internal static class PngCodec
{
    internal const ulong MaxPngPixels = 64UL * 1024 * 1024;

    internal const ulong MaxPngInputBytes = 64UL * 1024 * 1024;

    internal const uint BytesPerPixel = 4;

    internal static byte[] EncodeBgraToPng(byte[] pixels, uint width, uint height, uint stride, Deadline deadline)
    {
        EnsureRemaining(deadline);
        var needed = ValidateEncodeInput(pixels, width, height, stride);
        return EncodeBgraToPngWic(pixels.AsSpan(0, needed), width, height, stride);
    }

    internal static (byte[] Pixels, uint Width, uint Height) DecodePngToBgra(byte[] png, Deadline deadline)
    {
        EnsureRemaining(deadline);
        ValidateDecodeInput(png);
        return DecodePngToBgraWic(png);
    }

    internal static int ValidateEncodeInput(byte[] pixels, uint width, uint height, uint stride)
    {
        if (width == 0 || height == 0)
        {
            throw InvalidImage("BGRA dimensions must be non-zero");
        }

        var minStride = (ulong)width * BytesPerPixel;
        if (minStride > uint.MaxValue)
        {
            throw InvalidImage("BGRA width overflows the stride budget");
        }

        if (stride < minStride)
        {
            throw InvalidImage("BGRA stride is shorter than width times four bytes");
        }

        var pixelsCount = (ulong)width * height;
        if (pixelsCount > MaxPngPixels)
        {
            throw InvalidImage("BGRA dimensions exceed the pixel budget");
        }

        var needed = (ulong)stride * height;
        if (needed > MaxPngInputBytes)
        {
            throw InvalidImage("BGRA buffer exceeds the 64 MiB input budget");
        }

        if ((ulong)pixels.LongLength < needed)
        {
            throw InvalidImage("BGRA buffer is shorter than stride times height");
        }

        return (int)needed;
    }

    internal static void ValidateDecodeInput(byte[] png)
    {
        if (png.Length == 0)
        {
            throw InvalidImage("PNG payload is empty");
        }

        if ((ulong)png.LongLength > MaxPngInputBytes)
        {
            throw InvalidImage("PNG payload exceeds the 64 MiB encoded-data budget");
        }
    }

    /// <summary>Checks decoded dimensions against the pixel and byte budgets and returns the BGRA byte count.</summary>
    internal static int ValidateOutputDimensions(uint width, uint height)
    {
        if (width == 0 || height == 0)
        {
            throw InvalidImage("Decoded PNG reported zero dimensions");
        }

        var pixelsCount = (ulong)width * height;
        if (pixelsCount > MaxPngPixels)
        {
            throw InvalidImage("Decoded PNG dimensions exceed the pixel budget");
        }

        var bytes = (ulong)width * BytesPerPixel * height;
        if (bytes > MaxPngInputBytes)
        {
            throw InvalidImage("Decoded PNG buffer exceeds the 64 MiB input budget");
        }

        return (int)bytes;
    }

    /// <summary>
    /// SEAM: WIC encode (CoCreateInstance of CLSID_WICImagingFactory, CreateBitmapFromMemory, CreateEncoder for
    /// GUID_ContainerFormatPng, WriteSource, Commit, then read the in-memory stream). Not yet ported.
    /// </summary>
    private static byte[] EncodeBgraToPngWic(ReadOnlySpan<byte> pixels, uint width, uint height, uint stride)
    {
        throw AdapterError.NotSupported("PNG encoding through WIC");
    }

    /// <summary>
    /// SEAM: WIC decode (IWICImagingFactory CreateStream, InitializeFromMemory, CreateDecoderFromStream, GetFrame(0),
    /// format converter to 32bpp BGRA, CopyPixels). Not yet ported.
    /// </summary>
    private static (byte[] Pixels, uint Width, uint Height) DecodePngToBgraWic(byte[] png)
    {
        throw AdapterError.NotSupported("PNG decoding through WIC");
    }

    /// <summary>
    /// SEAM: the Rust codec calls Deadline::ensure_remaining, which core's C# Deadline does not expose. The
    /// expired check is the same refusal, raised before any WIC call, so nothing reaches the imaging factory.
    /// </summary>
    private static void EnsureRemaining(Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw AdapterError.Timeout("Operation budget is exhausted");
        }
    }

    private static AdapterError InvalidImage(string message)
    {
        return new AdapterError(ErrorCode.InvalidArgs, message);
    }
}
