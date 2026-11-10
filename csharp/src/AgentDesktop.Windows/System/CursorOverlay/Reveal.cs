namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Where the label card is in its appearance.
///
/// A card whose text has just changed is held back until the cursor has finished travelling and then eased in.
/// The reference reveals it after the movement rather than during it, so the eye follows the cursor to its
/// destination and reads the caption once it arrives. A card fading in alongside a moving cursor competes with it.
///
/// Kept apart from the host so the rule can be driven directly. Every transition here is reachable from a test,
/// which the host's own paint path is not.
/// </summary>
internal sealed class Reveal
{
    /// <summary>
    /// The length of the ease-in, in milliseconds. Mirrors the core constant CURSOR_LABEL_REVEAL_MS.
    /// </summary>
    internal const ulong CursorLabelRevealMs = 180;

    private bool pending;
    private bool playing;
    private long startedTimestamp;

    private Reveal(bool pending)
    {
        this.pending = pending;
    }

    /// <summary>
    /// A card with nothing waiting to appear: it is already there, so it is drawn at full opacity from the first
    /// frame. This is the state a control starts in, and the state a label leaves the card in when it goes away.
    /// </summary>
    public static Reveal Settled => new(pending: false);

    /// <summary>
    /// What a label change means for the card: text arriving has to appear, text going away has nothing to appear.
    /// </summary>
    public static Reveal ForLabel(string? label) => new(pending: label is not null);

    /// <summary>
    /// Starts the ease, if a card is waiting to appear. Called once the cursor has landed.
    /// </summary>
    public void Begin()
    {
        if (pending)
        {
            pending = false;
            playing = true;
            startedTimestamp = global::System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// How opaque the card should be drawn right now.
    /// </summary>
    public double Opacity()
    {
        if (playing)
        {
            return Schedule.RevealProgress(ElapsedMilliseconds(), CursorLabelRevealMs);
        }

        return pending ? 0.0 : 1.0;
    }

    /// <summary>
    /// Whether the ease is still running, so a caller knows to keep drawing frames rather than leaving the card
    /// half-arrived.
    /// </summary>
    public bool IsPlaying() => playing && ElapsedMilliseconds() < CursorLabelRevealMs;

    private ulong ElapsedMilliseconds()
    {
        var ticks = global::System.Diagnostics.Stopwatch.GetTimestamp() - startedTimestamp;
        return (ulong)(ticks * 1000L / global::System.Diagnostics.Stopwatch.Frequency);
    }
}
