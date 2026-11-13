using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>The paint half of the renderer: one frame composed and dimmed, the present that carries it, and the clear that leaves the screen empty.</summary>
internal sealed partial class SurfaceHost
{
    /// <summary>
    /// One frame at full strength, with the card at whatever point of its reveal it has reached.
    /// </summary>
    private void Draw(double ripplePhase, Rect? target, double highlightOpacity) =>
        Paint(ripplePhase, target, highlightOpacity, 1.0);

    /// <summary>
    /// One frame, dimmed to <paramref name="overlayOpacity"/>.
    /// </summary>
    /// <remarks>
    /// Both fades happen here and after everything is drawn, which is forced rather than chosen: GDI writes the
    /// label's text with no alpha, so it may only be drawn where the card beneath it is already opaque. Fading
    /// the card before the text went on would put the glyphs onto transparency and then force them back to full
    /// strength — a card that appears to arrive complete while only its border catches up.
    /// <para>
    /// The two monitor lookups are not one lookup done twice. The first asks which screen the cursor is on, to
    /// lay the card out inside its work area; the second asks which screen the composed surface's own corner
    /// falls on, to place the window in that monitor's physical pixels. A card near an edge routinely straddles
    /// the two.
    /// </para>
    /// </remarks>
    private void Paint(double ripplePhase, Rect? target, double highlightOpacity, double overlayOpacity)
    {
        var screen = Monitors.MonitorForPoint(_topology.Monitors, _presentation.Pose)?.WorkArea
            ?? new Rect(0.0, 0.0, 1920.0, 1080.0);
        var frame = new Render.Frame
        {
            Tip = _presentation.Pose,
            Style = _presentation.ShownStyle(),
            RipplePhase = ripplePhase,
            Target = target,
            HighlightOpacity = highlightOpacity,
            Label = _presentation.BuildLabelPill(),
            Screen = screen,
        };
        var composed = Render.Compose(frame);
        if (composed.TextRect is { } textRect && _presentation.Label is { } caption)
        {
            OverlayText.DrawLabel(
                textRect,
                caption,
                [1.0, 1.0, 1.0],
                LabelPill.LabelFontPoints * _presentation.Style.Size());
        }

        if (composed.CardRect is { } card)
        {
            Fade.DimRegion(composed.Surface, card, CardOpacity());
        }

        Fade.DimSurface(composed.Surface, overlayOpacity);
        _window.Raise();
        PresentIgnoringFailure(composed);
        _window.Pump();
    }

    /// <summary>
    /// The present is best-effort: a surface that cannot be painted leaves the frame undrawn and the loop
    /// carries on, which is what the reference does with the same call.
    /// </summary>
    private void PresentIgnoringFailure(Render.Composed composed)
    {
        try
        {
            _window.Present(
                (int)composed.Origin.X,
                (int)composed.Origin.Y,
                composed.Surface.Width,
                composed.Surface.Height,
                composed.Surface.Pixels);
        }
        catch (AgentDesktop.Core.Response.AdapterError)
        {
        }
    }

    private void Clear()
    {
        try
        {
            _window.Present(0, 0, 1, 1, [0u]);
        }
        catch (AgentDesktop.Core.Response.AdapterError)
        {
        }

        _window.Pump();
        _caret.Hide();
    }

    /// <summary>
    /// Fades the whole overlay out and leaves the screen clear.
    /// </summary>
    /// <remarks>
    /// Blocking for the length of the fade matches the reference, and the span is short enough that a control
    /// arriving inside it waits out the remainder rather than being lost — the next <c>Apply</c> clears the
    /// resting flag and draws at full strength, so nothing has to unwind a half-finished fade.
    /// </remarks>
    private void FadeAway()
    {
        var steps = RestFadeSteps;
        var interval = global::System.TimeSpan.FromMilliseconds(CursorOverlayTiming.RestFadeMs / steps);
        for (uint step = 1; step <= steps; step++)
        {
            Paint(0.0, null, 0.0, Schedule.RestFade(step, steps));
            global::System.Threading.Thread.Sleep(interval);
        }

        Clear();
        _rest.Resting = true;
    }

}
