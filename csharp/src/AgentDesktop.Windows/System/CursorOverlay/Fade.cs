using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Dimming an already-composed region of the surface.
/// </summary>
/// <remarks>
/// macOS fades by animating a window's layer opacity and letting the compositor do the work. There is no equivalent
/// here: the overlay is one layered window presented with <c>UpdateLayeredWindow</c>, so a fade has to be in the pixels.
///
/// It runs after everything else, and that ordering is the whole reason this is a separate pass rather than an opacity
/// threaded through the rasterizer. GDI draws the label's text with no alpha at all, so the text may only be written
/// where the card beneath it is already opaque; fading the card first would leave the glyphs to be drawn onto
/// transparency and then forced back to opaque, which is the card reappearing at full strength with only its border
/// faded. Draw at full strength, then dim what was drawn.
/// </remarks>
internal static class Fade
{
    /// <summary>
    /// Multiplies every channel of every pixel in <paramref name="rect"/> by <paramref name="factor"/>.
    /// </summary>
    /// <remarks>
    /// The surface is premultiplied, so alpha and colour scale together and a uniform multiply is the correct dim.
    /// Scaling alpha alone would leave the colour too strong and the region would darken as it faded rather than
    /// receding.
    /// </remarks>
    public static void DimRegion(Raster.Surface surface, Rect rect, double factor)
    {
        if (factor >= 1.0)
        {
            return;
        }

        factor = global::System.Math.Clamp(factor, 0.0, 1.0);
        int left = ToInt32Saturating(RustMax(global::System.Math.Floor(rect.X), 0.0));
        int top = ToInt32Saturating(RustMax(global::System.Math.Floor(rect.Y), 0.0));
        int right = ToInt32Saturating(RustMin(global::System.Math.Ceiling(rect.X + rect.Width), surface.Width));
        int bottom = ToInt32Saturating(RustMin(global::System.Math.Ceiling(rect.Y + rect.Height), surface.Height));

        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                long index = (long)y * surface.Width + x;
                if (index < 0 || index >= surface.Pixels.Length)
                {
                    continue;
                }

                surface.Pixels[index] = DimPixel(surface.Pixels[index], factor);
            }
        }
    }

    /// <summary>
    /// The whole surface, for the fade the overlay plays when it rests.
    /// </summary>
    public static void DimSurface(Raster.Surface surface, double factor)
    {
        Rect bounds = new(0.0, 0.0, surface.Width, surface.Height);
        DimRegion(surface, bounds, factor);
    }

    private static uint DimPixel(uint pixel, double factor)
    {
        return ScaleChannel(pixel, 24, factor)
            | ScaleChannel(pixel, 16, factor)
            | ScaleChannel(pixel, 8, factor)
            | ScaleChannel(pixel, 0, factor);
    }

    private static uint ScaleChannel(uint pixel, int shift, double factor)
    {
        double channel = (pixel >> shift) & 0xFF;
        double scaled = global::System.Math.Round(channel * factor, global::System.MidpointRounding.AwayFromZero);
        scaled = global::System.Math.Clamp(scaled, 0.0, 255.0);
        uint value = double.IsNaN(scaled) ? 0u : (uint)scaled;
        return value << shift;
    }

    private static double RustMax(double value, double floor)
    {
        if (double.IsNaN(value))
        {
            return floor;
        }

        return global::System.Math.Max(value, floor);
    }

    private static double RustMin(double value, double ceiling)
    {
        if (double.IsNaN(value))
        {
            return ceiling;
        }

        return global::System.Math.Min(value, ceiling);
    }

    private static int ToInt32Saturating(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        return (int)value;
    }
}
