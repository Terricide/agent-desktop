using System;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// A PNG drawn in place of the built-in cursor arrow. The hotspot is the click
/// point, in image pixels from the image's top-left corner.
/// </summary>
public sealed record CursorImage(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("hotspot")] Point Hotspot)
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const int PngHeaderBytes = 24;

    /// <summary>The image with no path and the hotspot at the image corner, which the wire omits.</summary>
    public static CursorImage Origin => new(string.Empty, new Point(0.0, 0.0));

    /// <summary>
    /// The validated image: a non-empty absolute PNG path within the escaped
    /// length ceiling, and a finite hotspot inside the hotspot ceiling. Throws
    /// when the image cannot be drawn.
    /// </summary>
    public CursorImage Validated()
    {
        if (Path.Length == 0 || Path.Contains('\0'))
            throw AdapterError.InvalidArgs("Cursor image path must be a non-empty file path").Unsent();

        if (JsonEscapedLength(Path) > CursorOverlayLimits.MaxPathBytes)
            throw AdapterError.InvalidArgs(
                $"Cursor image path must be at most {CursorOverlayLimits.MaxPathBytes} bytes once JSON-escaped").Unsent();

        if (System.IO.Path.GetPathRoot(Path) is not { Length: > 0 })
            throw AdapterError.InvalidArgs("Cursor image path must be absolute").Unsent();

        if (!string.Equals(System.IO.Path.GetExtension(Path), ".png", StringComparison.OrdinalIgnoreCase))
            throw AdapterError.InvalidArgs("cursor images must be PNG").Unsent();

        if (!HotspotInside(Hotspot))
            throw AdapterError.InvalidArgs(
                $"Cursor image --hotspot must be between 0 and {CursorOverlayLimits.MaxHotspot} on both axes").Unsent();

        return this;
    }

    /// <summary>
    /// Checks that the file is a regular file within the size limit, carries the
    /// PNG signature, and declares at most the pixel ceiling per side. Every
    /// check reads the same open file.
    /// </summary>
    public void VerifyFile()
    {
        byte[] header = new byte[PngHeaderBytes];
        long length;
        try
        {
            using var file = File.OpenRead(Path);
            length = file.Length;
            file.ReadExactly(header);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw AdapterError.InvalidArgs($"Cursor image '{Path}' cannot be read")
                .WithPlatformDetail(exception.Message)
                .Unsent();
        }

        if (length > CursorOverlayLimits.MaxImageBytes)
            throw AdapterError.InvalidArgs(
                $"Cursor image must be at most {CursorOverlayLimits.MaxImageBytes / (1024 * 1024)} MiB").Unsent();

        if (!header.AsSpan(0, PngMagic.Length).SequenceEqual(PngMagic))
            throw AdapterError.InvalidArgs("cursor images must be PNG").Unsent();

        if (!PngDimensionsFit(header))
            throw AdapterError.InvalidArgs(
                $"Cursor image PNG must be at most {CursorOverlayLimits.MaxImagePixels} pixels per side").Unsent();
    }

    /// <summary>
    /// The length the path occupies as JSON text, without the surrounding
    /// quotes, counted in UTF-8 bytes with each escape expanded.
    /// </summary>
    internal static int JsonEscapedLength(string text)
    {
        var bytes = Encoding.UTF8.GetByteCount(text);
        for (var index = 0; index < text.Length; index++)
        {
            var rune = text[index];
            if (rune is '"' or '\\' or '\b' or '\f' or '\n' or '\r' or '\t')
                bytes += 1;
            else if (rune < 0x20)
                bytes += 5;
        }

        return bytes;
    }

    private static bool HotspotInside(Point hotspot) =>
        IsInside(hotspot.X) && IsInside(hotspot.Y);

    private static bool IsInside(double value) =>
        double.IsFinite(value) && value >= 0.0 && value <= CursorOverlayLimits.MaxHotspot;

    private static bool PngDimensionsFit(byte[] header)
    {
        if (header[12] != 'I' || header[13] != 'H' || header[14] != 'D' || header[15] != 'R')
            return false;

        var width = Side(header, 16);
        var height = Side(header, 20);
        return width >= 1 && width <= CursorOverlayLimits.MaxImagePixels
            && height >= 1 && height <= CursorOverlayLimits.MaxImagePixels;
    }

    private static int Side(byte[] header, int at) =>
        (header[at] << 24) | (header[at + 1] << 16) | (header[at + 2] << 8) | header[at + 3];
}
