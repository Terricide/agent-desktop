using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The renderer's state: where the cursor is, what style it wears, and what it does when a control arrives.
/// </summary>
/// <remarks>
/// Core owns the trajectory. <c>CursorMotion</c> is a function of elapsed milliseconds, so this samples a clock
/// rather than counting frames — a dropped frame changes smoothness and never the instant the cursor lands, which
/// is the instant the action is waiting on.
/// <para>
/// A control is served in two halves. <c>Apply</c> does only what has to be true before its sender is answered:
/// the style and the caption are adopted, and a travel puts the cursor at its destination. <c>Settle</c> plays
/// what nothing waits on — the card easing in, the click flourish — after the answer has gone out and the
/// connection has been released, so the next control is already being read while the last one is still finishing
/// on screen.
/// </para>
/// </remarks>
internal sealed partial class SurfaceHost : IDisposable
{
    /// <summary>
    /// Thirteen hops, matching the reference, which is enough that the fade reads as continuous without costing
    /// a frame per pixel row.
    /// </summary>
    private const uint RestFadeSteps = 13;

    private OverlayWindow _window = null!;
    private CaretMark _caret = null!;
    private string _sessionId = null!;
    private SessionState.EndWatch _watch = new();
    private Presentation _presentation = null!;
    private RestState _rest = null!;
    private Topology _topology = null!;

    /// <summary>
    /// Reads the desktop once, picks where the cursor rests on it, and makes the two surfaces the renderer
    /// draws onto. A window that cannot be created is an internal error, because the renderer has nothing to
    /// draw onto without it.
    /// </summary>
    internal static SurfaceHost Create(string sessionId)
    {
        var topology = Topology.Probe();
        var pose = Monitors.RestingPoint(topology.Monitors) ?? new Point(0.0, 0.0);
        var host = new SurfaceHost();
        host._window = OverlayWindow.Create();
        host._caret = CaretMark.Create();
        host._sessionId = sessionId;
        host._presentation = Presentation.RestingAt(pose);
        host._rest = RestState.Quiet();
        host._watch = new SessionState.EndWatch();
        host._topology = topology;
        return host;
    }

    /// <summary>
    /// How often the connection is re-offered while nothing is on the wire, so a quiet renderer still notices
    /// the session ending and still fades itself away.
    /// </summary>
    internal TimeSpan IdleTick() =>
        global::System.TimeSpan.FromMilliseconds(CursorOverlayTiming.IdleRestMs / 4);

    /// <summary>
    /// Everything that must be true before the control's sender is answered, and nothing else.
    /// </summary>
    /// <remarks>
    /// A travel is here because the action dispatches the moment its acknowledgement arrives, so the cursor has
    /// to already be where the click will land.
    /// </remarks>
    internal void Apply(CursorOverlayControl control)
    {
        if (_rest.Resting && control.IsCaretFollow)
        {
            return;
        }

        // The label is drawn by GDI into the overlay surface, which is not ported. A control that asks
        // for one must fail closed here, before any presentation state moves, so a refused apply leaves
        // the overlay exactly as it was rather than partway through a reveal.
        RequireLabelPath(control);

        _rest.MarkQuiet();
        if (control.Style is { } style)
        {
            _presentation.Style = style.Copy();
        }

        AdoptLabel(control);
        switch (control.Action)
        {
            case CursorOverlayControlAction.Enable:
            case CursorOverlayControlAction.Show:
                BeginReveal();
                Draw(0.0, null, 0.0);
                break;
            case CursorOverlayControlAction.Hide:
            case CursorOverlayControlAction.Disable:
                Clear();
                break;
            case CursorOverlayControlAction.Present:
                ApplyPresent(control.Instruction);
                break;
        }
    }

    /// <summary>
    /// Everything nothing waits on, played after the sender has been answered and the connection released.
    /// </summary>
    /// <remarks>
    /// <paramref name="interrupt"/> reports that a newer control is already on the wire. Honouring it keeps a
    /// flourish from spending its hold on an action that has already been superseded, and keeps the sender of
    /// that newer control from spending its arrival budget on a busy pipe.
    /// </remarks>
    internal void Settle(CursorOverlayControl control, global::System.Func<bool> interrupt)
    {
        switch (control.Action)
        {
            case CursorOverlayControlAction.Enable:
            case CursorOverlayControlAction.Show:
                PlayReveal(null, interrupt);
                break;
            case CursorOverlayControlAction.Present:
                SettlePresent(control.Instruction, interrupt);
                break;
            case CursorOverlayControlAction.Hide:
            case CursorOverlayControlAction.Disable:
                break;
        }
    }

    /// <summary>
    /// One quiet tick: pump the window, re-read the display topology, and fade the overlay away once it has
    /// been quiet long enough.
    /// </summary>
    /// <remarks>
    /// The monitors and refresh rate are sampled when the renderer starts and would otherwise stay fixed for its
    /// whole life, so a resolution change, a scale change or a monitor plugged in mid-session would leave every
    /// later frame mapped against a desktop that no longer exists. Re-probing on the idle tick is how that is
    /// noticed: the window procedure is a plain <c>DefWindowProcW</c> and handles no topology message, and a
    /// message handler could not reach this state anyway without sharing it across the callback boundary.
    /// </remarks>
    internal void Rest()
    {
        _window.Pump();
        _topology.Reprobe();
        if (_rest.Resting)
        {
            return;
        }

        if (_rest.ElapsedMilliseconds() < CursorOverlayTiming.IdleRestMs)
        {
            return;
        }

        FadeAway();
    }

    /// <summary>
    /// True once the session has read finished twice running.
    /// </summary>
    internal bool SessionFinished()
    {
        var reading = SessionState.Classify(SessionState.ReadManifest(_sessionId));
        return _watch.Observe(reading);
    }

    /// <summary>
    /// Adopts the label the control carries, and only from a control that carries one.
    /// </summary>
    /// <remarks>
    /// <c>Enable</c> and <c>Present</c> say what the card should read; <c>Hide</c>, <c>Show</c> and
    /// <c>Disable</c> say nothing about it, and a <c>Show</c> after a <c>Hide</c> has to bring back the card
    /// that was there. So the label is replaced wholesale by those two — including with nothing, which is how a
    /// card goes away — and left alone by the rest. It used to be assigned only when a control carried one, so
    /// the first label ever set stayed on screen for the life of the renderer and every later action was
    /// narrated by a stale caption. It is bounded on the way in because this is the reading side of a pipe:
    /// what arrives has passed the transport's frame cap and nothing else.
    /// </remarks>
    /// <summary>Whether a control needs the unported GDI text path. Mirrors what <c>AdoptLabel</c>
    /// resolves, so the gate refuses exactly the controls that would have drawn glyphs.</summary>
    private void RequireLabelPath(CursorOverlayControl control)
    {
        if (!(control.IsEnable || control.IsPresent))
        {
            return;
        }

        if (_presentation.LabelFrom(control) is { } asked && Label.Clamp(asked) is not null)
        {
            throw AdapterError.NotSupported("cursor overlay label");
        }
    }

    private void AdoptLabel(CursorOverlayControl control)
    {
        if (!(control.IsEnable || control.IsPresent))
        {
            return;
        }

        var next = _presentation.LabelFrom(control) is { } asked ? Label.Clamp(asked) : null;
        if (next != _presentation.Label)
        {
            _presentation.Reveal = Reveal.ForLabel(next);
        }

        _presentation.Label = next;
    }

    /// <summary>
    /// Starts the card easing in, if one is waiting to.
    /// </summary>
    private void BeginReveal() => _presentation.Reveal.Begin();

    /// <summary>
    /// How far through its reveal the card is, or fully present when it has never been revealed.
    /// </summary>
    /// <summary>Releases the overlay surface. A host that is left standing keeps a real tool window
    /// owned by this process, which every later window enumeration in the process sees: the cursor
    /// overlay carries <c>WS_EX_TOOLWINDOW</c>, so a leaked host makes a menu probe read a window that
    /// is not pumping messages.</summary>
    public void Dispose()
    {
        _window.Dispose();
        _caret.Dispose();
    }

    internal double CardOpacity() => _presentation.Reveal.Opacity();
}
