using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The label's text, and the one place GDI is allowed to draw. The text is set in Segoe UI at semibold and
/// measured in that same font, so the pill core sizes from the measurement fits what is drawn, and is centred
/// in it across and down.
///
/// GDI writes RGB and leaves the alpha byte at zero, which is why every other element is composited by hand.
/// Text is the exception because a glyph rasterizer is not something to hand-roll: it is drawn into a scratch
/// surface, then copied into the overlay's buffer at full opacity, over a bubble body already composited
/// opaque. Forcing alpha across that inset rectangle is correct rather than a workaround — the rounded corners
/// it would otherwise square off are outside it.
/// </summary>
internal static class OverlayText
{
    private const int SemiBoldWeight = 600;
    private const int DefaultCharset = 1;
    private const int OutDefaultPrecis = 0;
    private const int ClipDefaultPrecis = 0;
    private const int AntialiasedQuality = 4;
    private const int DefaultPitchAndFamily = 0;

    /// <summary>
    /// The label's width in pixels when set at <paramref name="points"/>, or <c>null</c> when it cannot be
    /// measured; a label that cannot be measured is not drawn.
    /// </summary>
    internal static double? MeasureLabel(string label, double points)
    {
        int length = label.Length;
        IntPtr memory = CreateCompatibleDC(IntPtr.Zero);
        if (memory == IntPtr.Zero)
        {
            return null;
        }

        IntPtr font = LabelFont(points);
        IntPtr previous = SelectObject(memory, font);
        int measured = GetTextExtentPoint32W(memory, label, length, out WinSize extent);
        SelectObject(memory, previous);
        DeleteObject(font);
        DeleteDC(memory);

        if (measured == 0)
        {
            return null;
        }

        return extent.Cx;
    }

    /// <summary>
    /// Draws the label inside <paramref name="rect"/>, centred across and down, at full opacity. The text is
    /// drawn by GDI into a scratch surface pre-filled with the bubble body already composited under it, and
    /// copied back over that same rectangle, so the copied pixels carry full alpha. Pixels outside the
    /// overlay's buffer are skipped, and the inset rectangle never reaches the bubble's rounded corners, so
    /// forcing alpha over it squares off nothing.
    ///
    /// The overlay surface and the scratch DIB section are not ported yet, so this throws until they are.
    /// </summary>
    internal static void DrawLabel(Rect rect, string label, double[] rgb, double points)
    {
        throw AdapterError.NotSupported("cursor overlay label");
    }

    /// <summary>
    /// The label colour as a GDI <c>COLORREF</c>: red in the low byte, green in the middle, blue in the high
    /// one. Each channel is a unit fraction clamped to the range before it is scaled.
    /// </summary>
    internal static uint ColorRef(double[] rgb)
    {
        uint red = Channel(rgb[0]);
        uint green = Channel(rgb[1]);
        uint blue = Channel(rgb[2]);
        return red | (green << 8) | (blue << 16);
    }

    /// <summary>
    /// GDI batches drawing calls, so <c>DrawTextW</c> returning is no guarantee its output has reached the
    /// scratch surface's bits yet. <c>GdiFlush</c> is the documented way to force that before the buffer is
    /// read directly; without it the read can come back blank or partial, non-deterministically, with no
    /// error and no way to tell a successful draw from an unflushed one.
    /// </summary>
    internal static void FlushBeforeReadingBits()
    {
        GdiFlush();
    }

    /// <summary>
    /// Segoe UI at semibold, sized in pixels from <paramref name="points"/>. The height is negative so GDI
    /// sizes the face by its character height, not by the cell. It is created with antialiased quality rather
    /// than ClearType, because subpixel anti-aliasing composited through <c>UpdateLayeredWindow</c> against
    /// transparency fringes with colour.
    /// </summary>
    private static IntPtr LabelFont(double points)
    {
        return CreateFontW(
            -(int)Math.Round(points, MidpointRounding.AwayFromZero),
            0,
            0,
            0,
            SemiBoldWeight,
            false,
            false,
            false,
            DefaultCharset,
            OutDefaultPrecis,
            ClipDefaultPrecis,
            AntialiasedQuality,
            DefaultPitchAndFamily,
            "Segoe UI");
    }

    private static uint Channel(double value)
    {
        return (uint)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>The <c>SIZE</c> a text measurement returns: width and height in pixels.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize
    {
        internal int Cx;
        internal int Cy;
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int height,
        int width,
        int escapement,
        int orientation,
        int fontWeight,
        bool italic,
        bool underline,
        bool strikeOut,
        int charSet,
        int outputPrecision,
        int clipPrecision,
        int quality,
        int pitchAndFamily,
        string faceName);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetTextExtentPoint32W(IntPtr hdc, string lpString, int cch, out WinSize lpsize);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern void GdiFlush();
}
