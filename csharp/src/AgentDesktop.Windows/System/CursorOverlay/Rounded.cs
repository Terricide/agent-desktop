using AgentDesktop.Core;
using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Tree;
using static AgentDesktop.Windows.System.CursorOverlay.Raster;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The two rounded rectangles the overlay draws: the outline around the element being acted on, and the pill
/// the label's text sits on.
///
/// They share a shape and a hazard. Both are composited per-pixel for the reason Raster exists: GDI leaves the
/// alpha byte at zero and forcing alpha across a bounding rectangle would square off the corners these are
/// defined by. Both are described as a rounded rectangle minus a smaller one inset by their border.
///
/// The highlight is the hollow one, and hollowness is what makes it the expensive one: its bounding rectangle is
/// the element the caller clicked, so outlining a scroll container spans a hundred times the area of a button's
/// outline while painting no more of it. That is why it names the interior it will not paint rather than testing
/// every pixel of it.
/// </summary>
internal static class Rounded
{
    /// <summary>
    /// The outline around the element being acted on, at the opacity its own curve reports.
    /// </summary>
    internal static void DrawHighlight(
        Surface surface,
        SurfaceMapping mapping,
        Rect target,
        double opacity,
        double[] accent)
    {
        if (opacity <= 0.0)
        {
            return;
        }

        double scale = mapping.Scale;
        Rect outer = mapping.ToLocal(OverlayGeometry.HighlightRect(target, scale));
        double radius = OverlayGeometry.HighlightCornerRadius * scale;
        double border = OverlayGeometry.HighlightBorderWidth * scale;
        Rect inner = new Rect
        {
            X = outer.X + border,
            Y = outer.Y + border,
            Width = global::System.Math.Max(outer.Width - border * 2.0, 0.0),
            Height = global::System.Math.Max(outer.Height - border * 2.0, 0.0),
        };
        Paint paint = new Paint
        {
            Rgb = accent,
            Alpha = opacity,
        };
        Raster.FillRegionOutside(
            surface,
            outer,
            HollowInterior(outer, border, radius),
            paint,
            (x, y) =>
                InRoundedRect(outer, radius, x, y)
                && !InRoundedRect(inner, global::System.Math.Max(radius - border, 0.0), x, y));
    }

    /// <summary>
    /// The part of the outline's bounding rectangle the outline cannot reach.
    ///
    /// The ring lies between the outer and inner rectangles, so everything inside the inner rectangle is
    /// unpaintable, and the useful question is how much of it a rectangle can claim. The answer is not square:
    /// away from the corners the ring is only the border wide, and it bends inward by the corner radius solely at
    /// the four corners, which sit entirely within the full-width bands this leaves above and below. So the ends
    /// are held back by border plus radius and the sides by nothing but the border, and outlining a wide element
    /// walks two thin columns per row instead of two thick ones.
    ///
    /// Both insets carry a further border of slack over the boundary they have to clear, so this is a fact about
    /// the shape rather than a claim about floating point. Where the element is too small for the inner rectangle
    /// to have a corner radius that large, one extent or the other comes back negative, which encloses nothing, so
    /// a degenerate target paints through the same walk as every other.
    /// </summary>
    internal static Rect HollowInterior(Rect outer, double border, double radius)
    {
        double sides = border * 2.0;
        double ends = border + radius;
        return new Rect
        {
            X = outer.X + sides,
            Y = outer.Y + ends,
            Width = outer.Width - sides * 2.0,
            Height = outer.Height - ends * 2.0,
        };
    }

    /// <summary>
    /// The label pill: a soft shadow, then an opaque black, fully rounded body, drawn per-pixel so its ends keep
    /// their coverage. GDI writes the text on top, so the body has to be opaque wherever the text can land.
    /// </summary>
    internal static void DrawLabelPill(Surface surface, Rect rect, double scale)
    {
        double radius = rect.Height / 2.0;
        double offset = LabelPill.LabelShadowOffset * scale;
        double blur = global::System.Math.Max(LabelPill.LabelShadowBlur * scale, 0.5);
        Rect shadow = new Rect
        {
            X = rect.X,
            Y = rect.Y + offset,
            Width = rect.Width,
            Height = rect.Height,
        };
        Paint shadowPaint = new Paint
        {
            Rgb = new[] { 0.0, 0.0, 0.0 },
            Alpha = LabelPill.LabelShadowOpacity,
        };
        FillGraded(
            surface,
            OverlayGeometry.LabelShadowRect(rect, scale),
            shadowPaint,
            (x, y) => ShadowStrength(RoundedRectDistance(shadow, radius, x, y), blur));
        Paint bodyPaint = new Paint
        {
            Rgb = new[] { 0.0, 0.0, 0.0 },
            Alpha = 1.0,
        };
        Raster.FillRegion(
            surface,
            rect,
            bodyPaint,
            (x, y) => InRoundedRect(rect, radius, x, y));
    }

    /// <summary>
    /// Paints the paint across the bounds at the opacity the strength function gives each pixel's centre, for a
    /// soft edge such as a shadow, where coverage is a gradient rather than a shape.
    /// </summary>
    private static void FillGraded(
        Surface surface,
        Rect bounds,
        Paint paint,
        global::System.Func<double, double, double> strength)
    {
        int x0 = (int)global::System.Math.Max(global::System.Math.Floor(bounds.X), 0.0);
        int y0 = (int)global::System.Math.Max(global::System.Math.Floor(bounds.Y), 0.0);
        int x1 = (int)global::System.Math.Min(global::System.Math.Ceiling(bounds.X + bounds.Width), (double)surface.Width);
        int y1 = (int)global::System.Math.Min(global::System.Math.Ceiling(bounds.Y + bounds.Height), (double)surface.Height);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                double value = strength(x + 0.5, y + 0.5);
                surface.Blend(x, y, paint, value);
            }
        }
    }

    /// <summary>
    /// How strongly a shadow covers a point at the given distance outside its shape (negative inside): full well
    /// inside, none a blur's width outside, easing between.
    /// </summary>
    internal static double ShadowStrength(double distance, double blur)
    {
        double t = global::System.Math.Clamp((distance + blur) / (blur * 2.0), 0.0, 1.0);
        return 1.0 - t * t * (3.0 - 2.0 * t);
    }

    /// <summary>
    /// The signed distance from a point to a rounded rectangle's edge: negative inside, positive outside.
    /// </summary>
    internal static double RoundedRectDistance(Rect rect, double radius, double x, double y)
    {
        double clampedRadius = global::System.Math.Max(
            global::System.Math.Min(global::System.Math.Min(radius, rect.Width / 2.0), rect.Height / 2.0),
            0.0);
        double halfWidth = rect.Width / 2.0 - clampedRadius;
        double halfHeight = rect.Height / 2.0 - clampedRadius;
        double dx = global::System.Math.Abs(x - (rect.X + rect.Width / 2.0)) - halfWidth;
        double dy = global::System.Math.Abs(y - (rect.Y + rect.Height / 2.0)) - halfHeight;
        double outside = double.Hypot(global::System.Math.Max(dx, 0.0), global::System.Math.Max(dy, 0.0));
        return outside + global::System.Math.Min(global::System.Math.Max(dx, dy), 0.0) - clampedRadius;
    }

    /// <summary>
    /// Whether the point lies inside the rounded rectangle, with its corners rounded by the radius and clamped to
    /// half of the shorter side.
    /// </summary>
    internal static bool InRoundedRect(Rect rect, double radius, double x, double y)
    {
        if (rect.Width <= 0.0 || rect.Height <= 0.0)
        {
            return false;
        }

        double clampedRadius = global::System.Math.Max(
            global::System.Math.Min(global::System.Math.Min(radius, rect.Width / 2.0), rect.Height / 2.0),
            0.0);
        double left = rect.X + clampedRadius;
        double right = rect.X + rect.Width - clampedRadius;
        double top = rect.Y + clampedRadius;
        double bottom = rect.Y + rect.Height - clampedRadius;
        if (x < rect.X || y < rect.Y || x > rect.X + rect.Width || y > rect.Y + rect.Height)
        {
            return false;
        }

        double nearestX = global::System.Math.Clamp(x, left, right);
        double nearestY = global::System.Math.Clamp(y, top, bottom);
        return double.Hypot(x - nearestX, y - nearestY) <= clampedRadius;
    }
}
