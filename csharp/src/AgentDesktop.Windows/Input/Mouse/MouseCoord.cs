using System.Runtime.InteropServices;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Mouse;

/// <summary>Physical-pixel point to SendInput normalized-absolute coordinate transform.
/// MOUSEEVENTF_ABSOLUTE normalizes against the primary monitor only (A4-3, A10-6): a point outside
/// the primary rect needs MOUSEEVENTF_VIRTUALDESK, normalized against the full virtual screen instead.
/// Core hands physical-pixel points across the virtual screen (Point, from UIA BoundingRectangle under
/// PER_MONITOR_AWARE_V2), so every point received already speaks that space; the job here is choosing
/// which rect to normalize against and doing the division.</summary>
internal static class MouseCoord
{
    private const double NormalizedMax = 65535.0;

    private const int SmCxScreen = 0;

    private const int SmCyScreen = 1;

    private const int SmXVirtualScreen = 76;

    private const int SmYVirtualScreen = 77;

    private const int SmCxVirtualScreen = 78;

    private const int SmCyVirtualScreen = 79;

    /// <summary>A point in SendInput normalized space, with the desktop flag the send flags require.</summary>
    internal readonly record struct NormalizedPoint(int X, int Y, bool VirtualDesktop);

    internal static NormalizedPoint NormalizePoint(Point point)
    {
        var (primaryWidth, primaryHeight) = PrimaryScreenMetrics();
        if (WithinRect(point, 0, 0, primaryWidth, primaryHeight))
        {
            return new NormalizedPoint(
                NormalizeAxis(point.X, 0, primaryWidth),
                NormalizeAxis(point.Y, 0, primaryHeight),
                VirtualDesktop: false);
        }

        var (left, top, width, height) = VirtualScreenMetrics();
        return new NormalizedPoint(
            NormalizeAxis(point.X, left, width),
            NormalizeAxis(point.Y, top, height),
            VirtualDesktop: true);
    }

    internal static (int Width, int Height) PrimaryScreenMetrics() =>
        (GetSystemMetrics(SmCxScreen), GetSystemMetrics(SmCyScreen));

    /// <summary>The full virtual screen: the bounding rect of every monitor, origin included, so a
    /// point outside the primary rect can be normalized against the space it actually lives in.</summary>
    private static (int Left, int Top, int Width, int Height) VirtualScreenMetrics() =>
        (GetSystemMetrics(SmXVirtualScreen), GetSystemMetrics(SmYVirtualScreen),
         GetSystemMetrics(SmCxVirtualScreen), GetSystemMetrics(SmCyVirtualScreen));

    private static int NormalizeAxis(double coordinate, int origin, int extent)
    {
        if (extent <= 1)
        {
            return 0;
        }

        var relative = coordinate - origin;
        var scaled = (relative * NormalizedMax) / (extent - 1);
        var rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
        if (double.IsNaN(rounded))
        {
            return 0;
        }

        return (int)Math.Clamp(rounded, 0.0, NormalizedMax);
    }

    private static bool WithinRect(Point point, int left, int top, int width, int height) =>
        width > 0
        && height > 0
        && point.X >= left
        && point.X < (double)left + width
        && point.Y >= top
        && point.Y < (double)top + height;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
