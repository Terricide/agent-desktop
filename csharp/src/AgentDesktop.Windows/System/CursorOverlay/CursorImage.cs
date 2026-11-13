using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using static AgentDesktop.Windows.System.CursorOverlay.Raster;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// A caller's cursor image, drawn in place of the built-in dart. The PNG is decoded once per version of its file and
/// kept premultiplied, in the surface's own BGRA layout, so a frame only composites it. Each draw checks the file's
/// size and modification time, so a replaced PNG is decoded again and one that failed to decode is retried once it
/// changes. It is drawn at the style's size, with its hotspot on the pose point.
/// </summary>
internal static class CursorImage
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [ThreadStatic]
    private static Dictionary<string, CachedDecode>? decodedCache;

    /// <summary>
    /// The decoded image for <paramref name="image"/>, or <c>null</c> when its file is missing or not a readable PNG;
    /// the dart is drawn instead. A result is reused only while the file keeps the size and modification time it was
    /// decoded at.
    /// </summary>
    internal static DecodedImage? Decoded(global::AgentDesktop.Core.CursorOverlay.CursorImage image)
    {
        var path = image.Path;
        var stamp = ReadFileStamp(path);
        if (stamp is null)
        {
            return null;
        }

        var cache = decodedCache ??= new Dictionary<string, CachedDecode>(StringComparer.Ordinal);
        if (cache.TryGetValue(path, out var cached) && cached.Stamp == stamp.Value)
        {
            return cached.Decoded;
        }

        var decoded = Decode(path);
        cache[path] = new CachedDecode(stamp.Value, decoded);
        return decoded;
    }

    private static FileStamp? ReadFileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            return new FileStamp(info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception exception) when (IsFileReadFailure(exception))
        {
            return null;
        }
    }

    /// <summary>
    /// Where the image lands for a pose: its hotspot on the tip, scaled.
    /// </summary>
    internal static Rect ImageRect(Point tip, global::AgentDesktop.Core.CursorOverlay.CursorImage image, DecodedImage decoded, double scale)
    {
        var hotspot = image.Hotspot;
        return new Rect(
            tip.X - hotspot.X * scale,
            tip.Y - hotspot.Y * scale,
            decoded.Width * scale,
            decoded.Height * scale);
    }

    /// <summary>
    /// Composites the image over the surface, sampling the nearest source pixel for each destination pixel so a 1:1
    /// draw is exact.
    /// </summary>
    internal static void DrawImage(Surface surface, SurfaceMapping mapping, Rect at, DecodedImage decoded)
    {
        var local = mapping.ToLocal(at);
        if (local.Width <= 0 || local.Height <= 0)
        {
            return;
        }

        var left = Saturate(Math.Floor(local.X));
        var top = Saturate(Math.Floor(local.Y));
        var right = Saturate(Math.Ceiling(local.X + local.Width));
        var bottom = Saturate(Math.Ceiling(local.Y + local.Height));
        for (var y = Math.Max(top, 0); y < Math.Min(bottom, surface.Height); y++)
        {
            var v = Math.Floor((y + 0.5 - local.Y) / local.Height * decoded.Height);
            if (v < 0 || v >= decoded.Height)
            {
                continue;
            }

            for (var x = Math.Max(left, 0); x < Math.Min(right, surface.Width); x++)
            {
                var u = Math.Floor((x + 0.5 - local.X) / local.Width * decoded.Width);
                if (u < 0 || u >= decoded.Width)
                {
                    continue;
                }

                var source = decoded.Pixels[(int)v * decoded.Width + (int)u];
                Composite(surface, x, y, source);
            }
        }
    }

    /// <summary>
    /// Source-over for one premultiplied pixel onto the premultiplied surface.
    /// </summary>
    private static void Composite(Surface surface, int x, int y, uint source)
    {
        var sourceAlpha = source >> 24;
        if (sourceAlpha == 0)
        {
            return;
        }

        var index = y * surface.Width + x;
        if (index < 0 || index >= surface.Pixels.Length)
        {
            return;
        }

        var under = surface.Pixels[index];
        var keep = 255 - sourceAlpha;
        surface.Pixels[index] =
            (Blend(source, under, keep, 24) << 24)
            | (Blend(source, under, keep, 16) << 16)
            | (Blend(source, under, keep, 8) << 8)
            | Blend(source, under, keep, 0);
    }

    private static uint Blend(uint source, uint under, uint keep, int shift)
    {
        var over = (source >> shift) & 0xFF;
        var below = (under >> shift) & 0xFF;
        return Math.Min(over + (below * keep + 127) / 255, 255u);
    }

    private static DecodedImage? Decode(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (IsFileReadFailure(exception))
        {
            return null;
        }

        if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            using var source = new global::System.Drawing.Bitmap(stream);
            var bounds = new global::System.Drawing.Rectangle(0, 0, source.Width, source.Height);
            using var argb = source.Clone(bounds, global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            return ReadPremultiplied(argb);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or OutOfMemoryException or InvalidOperationException)
        {
            return null;
        }
    }

    private static DecodedImage ReadPremultiplied(global::System.Drawing.Bitmap argb)
    {
        var width = argb.Width;
        var height = argb.Height;
        var bounds = new global::System.Drawing.Rectangle(0, 0, width, height);
        var data = argb.LockBits(
            bounds,
            global::System.Drawing.Imaging.ImageLockMode.ReadOnly,
            global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var raw = new byte[stride * height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            var pixels = new uint[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = y * stride + x * 4;
                    pixels[y * width + x] = Premultiplied(raw[offset + 2], raw[offset + 1], raw[offset], raw[offset + 3]);
                }
            }

            return new DecodedImage(width, height, pixels);
        }
        finally
        {
            argb.UnlockBits(data);
        }
    }

    private static uint Premultiplied(byte red, byte green, byte blue, byte alpha)
    {
        static uint Scale(byte channel, uint alpha) => (channel * alpha + 127) / 255;

        var a = (uint)alpha;
        return (a << 24) | (Scale(red, a) << 16) | (Scale(green, a) << 8) | Scale(blue, a);
    }

    private static bool IsFileReadFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static int Saturate(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    private readonly record struct FileStamp(long Length, DateTime Modified);

    private sealed record CachedDecode(FileStamp Stamp, DecodedImage? Decoded);
}
