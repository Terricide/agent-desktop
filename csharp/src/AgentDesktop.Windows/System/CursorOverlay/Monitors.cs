using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Selects which monitor a point belongs to and where the overlay rests.
/// </summary>
/// <remarks>
/// These are pure functions over a supplied monitor list. The selection stays free of Win32 so a
/// test can present a mixed-DPI or multi-monitor arrangement that the desktop cannot show.
/// Points arrive in physical pixels because the CLI and the renderer are both per-monitor DPI
/// aware, so the overlay draws at them without scaling.
/// The list is crate-local rather than core's display info. Core carries no work area, and the
/// overlay needs the work area to place a label and to pick a resting point. Extending core would
/// change list-displays output on both platforms.
/// </remarks>
internal static class Monitors
{
    /// <summary>
    /// The monitor a point falls on, or the nearest one when it falls in the gap between two
    /// non-adjacent monitors. Nearest is by squared distance to the monitor's centre, which is
    /// deterministic for a tie rather than dependent on enumeration order.
    /// </summary>
    internal static OverlayMonitor? MonitorForPoint(IReadOnlyList<OverlayMonitor> monitors, Point point)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        foreach (OverlayMonitor monitor in monitors)
        {
            if (Contains(monitor.Bounds, point))
            {
                return monitor;
            }
        }

        OverlayMonitor nearest = monitors[0];
        for (int index = 1; index < monitors.Count; index++)
        {
            OverlayMonitor candidate = monitors[index];
            if (CompareDistance(nearest.Bounds, candidate.Bounds, point) > 0)
            {
                nearest = candidate;
            }
        }

        return nearest;
    }

    /// <summary>
    /// The primary monitor's work-area midpoint, where the cursor rests before any action has
    /// placed it.
    /// </summary>
    internal static Point? RestingPoint(IReadOnlyList<OverlayMonitor> monitors)
    {
        OverlayMonitor? primary = null;
        foreach (OverlayMonitor monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                primary = monitor;
                break;
            }
        }

        primary ??= monitors.Count > 0 ? monitors[0] : null;
        if (primary is null)
        {
            return null;
        }

        Rect area = primary.WorkArea;
        return new Point(area.X + area.Width / 2.0, area.Y + area.Height / 2.0);
    }

    private static bool Contains(Rect bounds, Point point) =>
        point.X >= bounds.X
        && point.X < bounds.X + bounds.Width
        && point.Y >= bounds.Y
        && point.Y < bounds.Y + bounds.Height;

    private static int CompareDistance(Rect left, Rect right, Point point)
    {
        double leftDistance = DistanceToCentre(left, point);
        double rightDistance = DistanceToCentre(right, point);
        if (leftDistance > rightDistance)
        {
            return 1;
        }

        return leftDistance < rightDistance ? -1 : 0;
    }

    private static double DistanceToCentre(Rect bounds, Point point)
    {
        double dx = point.X - (bounds.X + bounds.Width / 2.0);
        double dy = point.Y - (bounds.Y + bounds.Height / 2.0);
        return dx * dx + dy * dy;
    }
}

/// <summary>
/// One monitor as the overlay sees it: its full bounds, its work area, its scale and whether it is
/// the primary display. Dimensions are in physical pixels.
/// </summary>
internal sealed record OverlayMonitor(Rect Bounds, Rect WorkArea, double Scale, bool IsPrimary);
