using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// Converts a Win32 <c>RECT</c> into the product <see cref="Rect"/>. <c>RECT</c> names its far edges and
/// <c>Rect</c> names its size, so each axis takes a subtraction. The conversion lives in one place because
/// a swapped pair or a <c>left</c> written where a <c>top</c> belongs still compiles and still yields a
/// plausible rectangle that downstream code cannot tell from a real measurement.
/// </summary>
internal static class WinRect
{
    /// <summary>
    /// Width and height are derived rather than clamped: an inverted <c>RECT</c> is a read that went wrong,
    /// and reporting its negative size says so downstream instead of quietly collapsing it to nothing.
    /// </summary>
    public static Rect RectOf(NativeRect rect) =>
        new(
            X: rect.left,
            Y: rect.top,
            Width: rect.right - rect.left,
            Height: rect.bottom - rect.top);
}
