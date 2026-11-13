using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The overlay's shape, in the numbers the macOS renderer already uses.
/// Pure. The overlay claims Windows draws the same visual vocabulary as macOS, and a
/// claim like that needs numbers to fail against: core shares the motion,
/// the style and the label placement, but none of these dimensions. They are
/// carried across rather than invented, and scaled by the session's style
/// size so a caller that asks for a larger cursor gets a larger everything.
/// </summary>
internal static class OverlayGeometry
{
    /// <summary>
    /// The cursor glyph's box, and the four-point dart drawn inside it, with the
    /// tip at the first point, which is what the pose's position names.
    /// The dart is the macOS path flipped vertically, not copied. Core
    /// Graphics authors that path in a y-up space where the tip's 35.0 sits
    /// near the top of a 40-unit box; this surface is a top-down DIB where the
    /// same number is near the bottom, so carrying the coordinates across
    /// unchanged draws the pointer upside down. Flipping once here is what keeps
    /// every consumer, the glyph rectangle and the rasterizer both, in one
    /// space.
    /// </summary>
    internal const double GlyphWidth = 32.0;

    internal const double GlyphHeight = 40.0;

    internal const double GlyphRimWidth = 6.5;

    internal static readonly (double X, double Y)[] Dart =
    [
        (1.0, 5.0),
        (29.6, 22.5),
        (12.7, 23.7),
        (4.2, 38.4),
    ];

    /// <summary>
    /// The ripple's full extent, and the solid disc at its centre.
    /// </summary>
    internal const double RippleSize = 108.0;

    internal const double RippleCoreRadius = 19.0;

    internal const double RippleRingInset = 4.0;

    /// <summary>
    /// The highlight drawn around the element being acted on.
    /// </summary>
    internal const double HighlightBorderWidth = 2.5;

    internal const double HighlightCornerRadius = 8.0;

    internal const double HighlightPadding = 5.0;

    /// <summary>
    /// The glyph's rectangle for a pose, with the dart's tip at the pose point.
    /// </summary>
    internal static Rect GlyphRect(Point tip, double scale)
    {
        var (tipX, tipY) = Dart[0];
        return new Rect
        {
            X = tip.X - tipX * scale,
            Y = tip.Y - tipY * scale,
            Width = GlyphWidth * scale,
            Height = GlyphHeight * scale,
        };
    }

    /// <summary>
    /// The ripple's rectangle for a pose, centred on the pose point.
    /// </summary>
    internal static Rect RippleRect(Point tip, double scale)
    {
        var size = RippleSize * scale;
        return new Rect
        {
            X = tip.X - size / 2.0,
            Y = tip.Y - size / 2.0,
            Width = size,
            Height = size,
        };
    }

    /// <summary>
    /// The label pill's shadow extent: the pill's rectangle moved down by the
    /// shadow's offset and grown by its blur on every side, so the follower
    /// surface and the card's fade both cover the whole shadow.
    /// </summary>
    internal static Rect LabelShadowRect(Rect pill, double scale)
    {
        var blur = LabelPill.LabelShadowBlur * scale * 2.0;
        return new Rect
        {
            X = pill.X - blur,
            Y = pill.Y + LabelPill.LabelShadowOffset * scale - blur,
            Width = pill.Width + blur * 2.0,
            Height = pill.Height + blur * 2.0,
        };
    }

    /// <summary>
    /// The highlight's rectangle: the target's bounds, padded, so the outline
    /// sits outside the element rather than over its edge.
    /// </summary>
    internal static Rect HighlightRect(Rect target, double scale)
    {
        var padding = HighlightPadding * scale;
        return new Rect
        {
            X = target.X - padding,
            Y = target.Y - padding,
            Width = target.Width + padding * 2.0,
            Height = target.Height + padding * 2.0,
        };
    }

    /// <summary>
    /// The one surface the renderer paints, sized to contain everything this
    /// frame draws.
    /// A surface that spanned the virtual screen would cost a frame budget rather
    /// than a rounding error on a large desktop, measured near-linear in pixel
    /// count, so the window follows the pose instead, and has to be big enough
    /// for the ripple at full extent or the effect would be clipped at its edges.
    /// </summary>
    internal static Rect FollowerRect(Point tip, double scale, Rect? label, Rect? highlight)
    {
        var union = UnionOf(GlyphRect(tip, scale), RippleRect(tip, scale));
        if (label is Rect labelRect)
        {
            union = UnionOf(union, labelRect);
        }

        if (highlight is Rect highlightRect)
        {
            union = UnionOf(union, highlightRect);
        }

        return union;
    }

    internal static Rect UnionOf(Rect left, Rect right)
    {
        var x = global::System.Math.Min(left.X, right.X);
        var y = global::System.Math.Min(left.Y, right.Y);
        var rightEdge = global::System.Math.Max(left.X + left.Width, right.X + right.Width);
        var bottomEdge = global::System.Math.Max(left.Y + left.Height, right.Y + right.Height);
        return new Rect
        {
            X = x,
            Y = y,
            Width = rightEdge - x,
            Height = bottomEdge - y,
        };
    }
}
