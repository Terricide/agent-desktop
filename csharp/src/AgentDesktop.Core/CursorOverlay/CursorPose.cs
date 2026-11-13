using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// One frame of the drawn cursor: where it is, and how far its click ripple has run.
/// </summary>
public sealed record CursorPose
{
    /// <summary>The point the cursor is drawn at.</summary>
    public Point Point { get; init; } = new Point(0.0, 0.0);

    /// <summary>How far through the ripple the frame is, from 0 to 1.</summary>
    public double Ripple { get; init; }

    /// <summary>A frame with no ripple running.</summary>
    public static CursorPose Still(Point point) => new() { Point = point, Ripple = 0.0 };
}
