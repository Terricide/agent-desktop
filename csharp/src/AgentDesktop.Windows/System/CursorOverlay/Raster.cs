using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The overlay's pixels, composited by hand into a premultiplied buffer.
/// GDI raster primitives write RGB and leave the alpha byte at zero, so anything drawn through GDI is invisible under
/// ULW_ALPHA. Every shape the overlay draws is therefore composited here with coverage computed rather than approximated.
/// The alpha behaviour that makes this necessary is assertable without a window.
/// </summary>
internal static class Raster
{
    private const int MaxSurfacePixels = 16_777_216;
    private const int Samples = 3;
    private const double MachineEpsilon = 2.220446049250313e-16;
    private static readonly Rect NothingSkipped = new(0.0, 0.0, -1.0, -1.0);

    internal readonly record struct Paint(double[] Rgb, double Alpha);

    internal readonly record struct SurfaceMapping(Point Origin, double Scale)
    {
        /// <summary>A screen-space rectangle in surface coordinates.</summary>
        internal Rect ToLocal(Rect rect) =>
            new(rect.X - Origin.X, rect.Y - Origin.Y, rect.Width, rect.Height);
    }

    /// <summary>A premultiplied 32bpp BGRA surface, top-down, ready for UpdateLayeredWindow.</summary>
    internal sealed class Surface
    {
        private Surface(int width, int height, uint[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal uint[] Pixels { get; }

        internal static Surface? TryTransparent(int width, int height)
        {
            if (width < 0 || height < 0)
            {
                return null;
            }
            long count = (long)width * height;
            if (count > MaxSurfacePixels)
            {
                return null;
            }
            try
            {
                return new Surface(width, height, new uint[count]);
            }
            catch (global::System.OutOfMemoryException)
            {
                return null;
            }
        }

        internal static Surface Empty() => new(0, 0, global::System.Array.Empty<uint>());

        internal byte AlphaAt(int x, int y)
        {
            var value = PixelAt(x, y);
            return value is null ? (byte)0 : (byte)(value.Value >> 24);
        }

        internal uint? PixelAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return null;
            }
            return Pixels[(y * Width) + x];
        }

        /// <summary>Composites one sample over what is already there, premultiplied.</summary>
        internal void Blend(int x, int y, Paint paint, double coverage)
        {
            if (coverage <= 0.0 || x < 0 || y < 0 || x >= Width || y >= Height)
            {
                return;
            }
            var index = (y * Width) + x;
            var existing = Pixels[index];
            var sourceAlpha = global::System.Math.Clamp(paint.Alpha * coverage, 0.0, 1.0);
            if (sourceAlpha <= 0.0)
            {
                return;
            }
            var existingAlpha = (existing >> 24) / 255.0;
            var outAlpha = sourceAlpha + (existingAlpha * (1.0 - sourceAlpha));
            var blue = Channel(existing, 0, paint.Rgb[2], sourceAlpha);
            var green = Channel(existing, 8, paint.Rgb[1], sourceAlpha);
            var red = Channel(existing, 16, paint.Rgb[0], sourceAlpha);
            var alpha = RoundToByte(global::System.Math.Clamp(outAlpha, 0.0, 1.0));
            Pixels[index] = (alpha << 24) | (red << 16) | (green << 8) | blue;
        }

        private static uint Channel(uint existing, int shift, double source, double sourceAlpha)
        {
            var under = ((existing >> shift) & 0xFF) / 255.0;
            var over = (source * sourceAlpha) + (under * (1.0 - sourceAlpha));
            return RoundToByte(global::System.Math.Clamp(over, 0.0, 1.0));
        }

        private static uint RoundToByte(double unit) =>
            (uint)global::System.Math.Round(unit * 255.0, global::System.MidpointRounding.AwayFromZero);
    }

    /// <summary>The cursor glyph: a rimmed dart, its tip on the pose point.</summary>
    internal static void DrawGlyph(Surface surface, SurfaceMapping mapping, Point tip, double[] fill, double[] rim)
    {
        var scale = mapping.Scale;
        var rect = mapping.ToLocal(OverlayGeometry.GlyphRect(tip, scale));
        var dart = OverlayGeometry.Dart;
        var points = new (double X, double Y)[dart.Length];
        for (var i = 0; i < dart.Length; i++)
        {
            points[i] = (rect.X + (dart[i].X * scale), rect.Y + (dart[i].Y * scale));
        }
        var rimExtent = OverlayGeometry.GlyphRimWidth * scale;
        var local = new Rect(
            rect.X - rimExtent,
            rect.Y - rimExtent,
            rect.Width + (rimExtent * 2.0),
            rect.Height + (rimExtent * 2.0));
        var rimWidth = OverlayGeometry.GlyphRimWidth * scale * 0.5;

        FillRegion(
            surface,
            local,
            new Paint(rim, 1.0),
            (x, y) => PointInPolygon(points, x, y) || DistanceToPolygon(points, x, y) <= rimWidth);
        FillRegion(surface, local, new Paint(fill, 1.0), (x, y) => PointInPolygon(points, x, y));
    }

    /// <summary>The click ripple: a solid centre and an expanding ring, both fading as the ripple's progress advances.</summary>
    internal static void DrawRipple(Surface surface, SurfaceMapping mapping, Point tip, double progress, double[] accent)
    {
        if (progress <= 0.0 || progress > 1.0)
        {
            return;
        }
        var scale = mapping.Scale;
        var local = mapping.ToLocal(OverlayGeometry.RippleRect(tip, scale));
        var centreX = local.X + (local.Width / 2.0);
        var centreY = local.Y + (local.Height / 2.0);
        var fade = 1.0 - progress;

        var core = OverlayGeometry.RippleCoreRadius * scale * (1.0 - (progress * 0.4));
        FillRegion(
            surface,
            local,
            new Paint(accent, 0.55 * fade),
            (x, y) => double.Hypot(x - centreX, y - centreY) <= core);

        var ring = ((OverlayGeometry.RippleSize * 0.5) - OverlayGeometry.RippleRingInset) * scale * progress;
        var thickness = 3.0 * scale;
        FillRegion(
            surface,
            local,
            new Paint(accent, 0.85 * fade),
            (x, y) =>
            {
                var distance = double.Hypot(x - centreX, y - centreY);
                return distance <= ring && distance >= ring - thickness;
            });
    }

    internal static void FillRegion(
        Surface surface,
        Rect bounds,
        Paint paint,
        global::System.Func<double, double, bool> inside) =>
        FillRegionOutside(surface, bounds, NothingSkipped, paint, inside);

    /// <summary>
    /// The same walk, with a rectangle the caller has established the predicate is false across left unvisited.
    /// Skipping is per pixel so no pixel is visited twice; a skipped cell has zero coverage, so skipping it yields the same bytes.
    /// </summary>
    internal static void FillRegionOutside(
        Surface surface,
        Rect bounds,
        Rect skip,
        Paint paint,
        global::System.Func<double, double, bool> inside)
    {
        var x0 = SaturatingInt(global::System.Math.Max(global::System.Math.Floor(bounds.X), 0.0));
        var y0 = SaturatingInt(global::System.Math.Max(global::System.Math.Floor(bounds.Y), 0.0));
        var x1 = SaturatingInt(global::System.Math.Min(global::System.Math.Ceiling(bounds.X + bounds.Width), surface.Width));
        var y1 = SaturatingInt(global::System.Math.Min(global::System.Math.Ceiling(bounds.Y + bounds.Height), surface.Height));
        var skipX0 = SaturatingInt(global::System.Math.Max(global::System.Math.Ceiling(skip.X), x0));
        var skipX1 = SaturatingInt(global::System.Math.Min(global::System.Math.Floor(skip.X + skip.Width), x1));
        var skipY0 = SaturatingInt(global::System.Math.Max(global::System.Math.Ceiling(skip.Y), y0));
        var skipY1 = SaturatingInt(global::System.Math.Min(global::System.Math.Floor(skip.Y + skip.Height), y1));
        var hollow = skipX0 < skipX1 && skipY0 < skipY1;
        for (var y = y0; y < y1; y++)
        {
            if (hollow && y >= skipY0 && y < skipY1)
            {
                BlendRun(surface, x0, skipX0, y, paint, inside);
                BlendRun(surface, skipX1, x1, y, paint, inside);
            }
            else
            {
                BlendRun(surface, x0, x1, y, paint, inside);
            }
        }
    }

    private static void BlendRun(
        Surface surface,
        int from,
        int to,
        int y,
        Paint paint,
        global::System.Func<double, double, bool> inside)
    {
        for (var x = from; x < to; x++)
        {
            surface.Blend(x, y, paint, Coverage(x, y, inside));
        }
    }

    /// <summary>
    /// Coverage of one pixel. A pixel whose corners and centre all agree is not on an edge, so the sampling grid is skipped.
    /// The early-out is exact for a convex region; a feature thinner than a pixel could slip between the five probes, which is
    /// why ring widths stay above one pixel at the smallest style size the CLI accepts.
    /// </summary>
    private static double Coverage(int x, int y, global::System.Func<double, double, bool> inside)
    {
        var left = (double)x;
        var top = (double)y;
        var first = inside(left, top);
        var undecided =
            inside(left + 1.0, top) != first
            || inside(left, top + 1.0) != first
            || inside(left + 1.0, top + 1.0) != first
            || inside(left + 0.5, top + 0.5) != first;
        if (!undecided)
        {
            return first ? 1.0 : 0.0;
        }

        var hits = 0;
        for (var sy = 0; sy < Samples; sy++)
        {
            for (var sx = 0; sx < Samples; sx++)
            {
                var px = left + ((sx + 0.5) / Samples);
                var py = top + ((sy + 0.5) / Samples);
                if (inside(px, py))
                {
                    hits++;
                }
            }
        }
        return (double)hits / (Samples * Samples);
    }

    private static bool PointInPolygon((double X, double Y)[] points, double x, double y)
    {
        var inside = false;
        var j = points.Length - 1;
        for (var i = 0; i < points.Length; i++)
        {
            var (xi, yi) = points[i];
            var (xj, yj) = points[j];
            if ((yi > y) != (yj > y) && x < (((xj - xi) * (y - yi)) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
            j = i;
        }
        return inside;
    }

    private static double DistanceToPolygon((double X, double Y)[] points, double x, double y)
    {
        var best = double.MaxValue;
        var j = points.Length - 1;
        for (var i = 0; i < points.Length; i++)
        {
            best = global::System.Math.Min(best, DistanceToSegment(points[j], points[i], x, y));
            j = i;
        }
        return best;
    }

    private static double DistanceToSegment((double X, double Y) a, (double X, double Y) b, double x, double y)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared <= MachineEpsilon
            ? 0.0
            : global::System.Math.Clamp((((x - a.X) * dx) + ((y - a.Y) * dy)) / lengthSquared, 0.0, 1.0);
        return double.Hypot(x - (a.X + (t * dx)), y - (a.Y + (t * dy)));
    }

    /// <summary>Mirrors Rust's saturating float-to-int cast: NaN becomes zero, out-of-range values clamp.</summary>
    private static int SaturatingInt(double value) =>
        double.IsNaN(value)
            ? 0
            : (int)global::System.Math.Clamp(value, int.MinValue, int.MaxValue);
}
