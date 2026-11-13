using System;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The agent's text caret: a thin bar in the accent colour where the app is inserting text, in a layered window of its
/// own so it can sit in a text field far from wherever the cursor rests. It marks the field the cursor was typing in,
/// so it goes when the cursor sets off for somewhere else.
/// </summary>
internal sealed class CaretMark : IDisposable
{
    private const double CursorSamePoint = 1.5;

    private readonly OverlayWindow _window;

    private bool shown;

    private CaretMark(OverlayWindow window)
    {
        _window = window;
    }

    internal static CaretMark Create() => new(OverlayWindow.Create());

    /// <summary>
    /// Whether moving the cursor from <paramref name="from"/> to <paramref name="to"/> leaves the place it was:
    /// <c>CursorSamePoint</c> or more away, the way the macOS renderer decides.
    /// </summary>
    internal static bool Leaves(Point from, Point to) =>
        double.Hypot(to.X - from.X, to.Y - from.Y) >= CursorSamePoint;

    /// <summary>
    /// Draws the caret over <paramref name="at"/>. With <paramref name="follow"/>, only a caret already showing moves,
    /// and no <paramref name="at"/> hides it.
    /// </summary>
    internal void Place(Rect? at, double[] accent, bool follow)
    {
        if (follow && !shown)
        {
            return;
        }

        if (at is Rect target)
        {
            Show(target, accent);
        }
        else if (follow)
        {
            Hide();
        }
    }

    /// <summary>
    /// Draws the caret over <paramref name="at"/>. The CLI and the renderer are both per-monitor DPI aware, so
    /// <paramref name="at"/> is already in physical pixels.
    /// </summary>
    private void Show(Rect at, double[] accent)
    {
        int width = RoundPixel(at.Width) > 1 ? RoundPixel(at.Width) : 1;
        int height = RoundPixel(at.Height) > 1 ? RoundPixel(at.Height) : 1;
        uint pixel = (0xFFu << 24)
            | (Channel(accent[0]) << 16)
            | (Channel(accent[1]) << 8)
            | Channel(accent[2]);
        uint[] pixels = new uint[(long)width * height];
        Array.Fill(pixels, pixel);
        Present(RoundPixel(at.X), RoundPixel(at.Y), width, height, pixels);
        shown = true;
    }

    internal void Hide()
    {
        Present(0, 0, 0, 0, Array.Empty<uint>());
        shown = false;
    }

    /// <summary>
    /// Hides the caret when the cursor sets off from <paramref name="from"/> for <paramref name="to"/> somewhere else;
    /// a travel that stays put keeps it.
    /// </summary>
    internal void HideWhenLeaving(Point from, Point to)
    {
        if (Leaves(from, to))
        {
            Hide();
        }
    }

    /// <summary>
    /// Raises the caret's own layered window, presents the pixel buffer, and pumps the queue so the window keeps
    /// answering the system while the caret stands.
    /// </summary>
    private void Present(int x, int y, int width, int height, uint[] pixels)
    {
        _window.Raise();
        _window.Present(x, y, width, height, pixels);
        _window.Pump();
    }

    public void Dispose() => _window.Dispose();

    private static int RoundPixel(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static uint Channel(double value)
    {
        if (double.IsNaN(value))
        {
            return 0;
        }

        return (uint)Math.Round(Math.Clamp(value, 0.0, 1.0) * 255.0, MidpointRounding.AwayFromZero);
    }
}
