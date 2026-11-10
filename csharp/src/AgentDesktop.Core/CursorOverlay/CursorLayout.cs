using System;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// Where the cursor overlay draws. The label sits right of and below the point
/// the cursor targets, which clears the dart drawn below and right of its tip,
/// and flips to the far side when the preferred spot runs off the screen.
/// </summary>
public static class CursorLayout
{
    /// How close, in points, two cursor positions are to count as the same place.
    public const double SamePoint = 1.5;

    private const double LabelOffsetX = 20.0;
    private const double LabelOffsetY = 30.0;

    /// Places the label for a destination, keeping it inside the screen.
    public static Rect PlaceLabel(Point destination, (double Width, double Height) size, Rect screen)
    {
        var right = screen.X + screen.Width;
        var bottom = screen.Y + screen.Height;
        var preferred_x = destination.X + LabelOffsetX;
        var preferred_y = destination.Y + LabelOffsetY;
        var x = preferred_x + size.Width <= right ? preferred_x : destination.X - size.Width - LabelOffsetX;
        var y = preferred_y + size.Height <= bottom ? preferred_y : destination.Y - size.Height - LabelOffsetY;

        return new Rect
        {
            X = Math.Clamp(x, screen.X, Math.Max(right - size.Width, screen.X)),
            Y = Math.Clamp(y, screen.Y, Math.Max(bottom - size.Height, screen.Y)),
            Width = Math.Min(size.Width, Math.Max(screen.Width, 0.0)),
            Height = Math.Min(size.Height, Math.Max(screen.Height, 0.0)),
        };
    }
}
