using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using static AgentDesktop.Windows.System.CursorOverlay.Raster;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// One frame: where the follower surface sits, and what is on it. Pure apart from the label's text, which the caller
/// measures beforehand and draws afterwards onto the pill this composes. Keeping the composition itself free of Win32
/// is what lets the whole visual layer be asserted without a desktop.
/// </summary>
internal static class Render
{
    /// <summary>
    /// How far the text rectangle stays inside the pill's top and bottom edges. GDI's text is copied in at full opacity
    /// across that whole rectangle, so it has to lie where the rounded body is already opaque.
    /// </summary>
    private const double TextVerticalInset = 2.0;

    /// <summary>
    /// Everything a frame needs, resolved from the control and the clock before any pixel is touched.
    /// </summary>
    internal sealed record Frame
    {
        public required Point Tip { get; init; }

        public required CursorOverlayStyle Style { get; init; }

        public double RipplePhase { get; init; }

        public Rect? Target { get; init; }

        public double HighlightOpacity { get; init; }

        /// <summary>
        /// The label's pill, sized to its measured text, when there is a label.
        /// </summary>
        public LabelPill? Label { get; init; }

        public Rect Screen { get; init; }
    }

    /// <summary>
    /// The composed frame. <paramref name="TextRect"/> is where the label's text goes, in surface coordinates, when there
    /// is one. The caller draws into it and then forces it opaque, which is the only region GDI is allowed to touch.
    /// <paramref name="CardRect"/> is the whole card and its shadow, in surface coordinates, when there is one. The text
    /// rectangle is only its interior; a caller fading the card in has to dim the pill and its shadow with the text, or
    /// the card arrives already drawn with only its writing catching up.
    /// </summary>
    internal sealed record Composed(Point Origin, Surface Surface, Rect? TextRect, Rect? CardRect);

    internal static Composed Compose(Frame frame)
    {
        var scale = frame.Style.Size();
        var image = PlaceImage(frame, scale);
        Rect? labelRect = frame.Label is { } pill
            ? CursorLayout.PlaceLabel(frame.Tip, pill.Size, frame.Screen)
            : null;
        Rect? cardBounds = labelRect is { } label
            ? OverlayGeometry.UnionOf(label, OverlayGeometry.LabelShadowRect(label, scale))
            : null;
        Rect? highlighted = frame.Target is { } target && frame.Style.Highlight() && frame.HighlightOpacity > 0.0
            ? target
            : null;
        Rect? highlightRect = highlighted is { } highlight ? OverlayGeometry.HighlightRect(highlight, scale) : null;

        var bounds = OverlayGeometry.FollowerRect(frame.Tip, scale, cardBounds, highlightRect);
        if (image is { } placed)
        {
            bounds = OverlayGeometry.UnionOf(bounds, placed.Rect);
        }

        var originX = global::System.Math.Floor(bounds.X);
        var originY = global::System.Math.Floor(bounds.Y);
        var mapping = new SurfaceMapping
        {
            Origin = new Point(originX, originY),
            Scale = scale,
        };
        var surface = SurfaceExtent(bounds) is { } extent
            ? Surface.TryTransparent(extent.Width, extent.Height)
            : null;
        if (surface is null)
        {
            return new Composed(mapping.Origin, Surface.Empty(), null, null);
        }

        if (highlighted is { } targetRect)
        {
            Rounded.DrawHighlight(surface, mapping, targetRect, frame.HighlightOpacity, frame.Style.AccentRgb());
        }
        if (frame.Style.Ripple())
        {
            Raster.DrawRipple(surface, mapping, frame.Tip, frame.RipplePhase, frame.Style.AccentRgb());
        }

        Rect? cardRect = cardBounds is { } card ? mapping.ToLocal(card) : null;
        Rect? textRect = null;
        if (labelRect is { } labelBox && frame.Label is { } labelPill)
        {
            var local = mapping.ToLocal(labelBox);
            Rounded.DrawLabelPill(surface, local, scale);
            var line = labelPill.Within(local.Width).TextFrame(local.Height - TextVerticalInset * 2.0 * scale);
            textRect = line with { X = local.X + line.X, Y = local.Y + line.Y };
        }

        if (image is { } drawn)
        {
            CursorImage.DrawImage(surface, mapping, drawn.Rect, drawn.Decoded);
        }
        else
        {
            Raster.DrawGlyph(surface, mapping, frame.Tip, frame.Style.FillRgb(), frame.Style.RimRgb());
        }

        return new Composed(mapping.Origin, surface, textRect, cardRect);
    }

    private static (DecodedImage Decoded, Rect Rect)? PlaceImage(Frame frame, double scale)
    {
        if (frame.Style.Image() is not { } source)
        {
            return null;
        }

        if (CursorImage.Decoded(source) is not { } decoded)
        {
            return null;
        }

        return (decoded, CursorImage.ImageRect(frame.Tip, source, decoded, scale));
    }

    private static (int Width, int Height)? SurfaceExtent(Rect bounds)
    {
        var width = Extent(bounds.Width);
        var height = Extent(bounds.Height);
        if (width is null || height is null)
        {
            return null;
        }

        return (width.Value, height.Value);
    }

    private static int? Extent(double value)
    {
        var pixels = global::System.Math.Ceiling(value);
        if (!double.IsFinite(pixels) || pixels < 0.0 || pixels >= int.MaxValue)
        {
            return null;
        }

        return (int)pixels + 1;
    }
}
