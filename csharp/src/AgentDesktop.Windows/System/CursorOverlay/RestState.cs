namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Whether the overlay has faded out, and how long it has been quiet.
/// </summary>
/// <remarks>
/// The overlay does not sit on screen indefinitely after the last instruction: it fades and orders itself away,
/// and the next control brings it straight back at full strength.
/// </remarks>
internal sealed class RestState
{
    private long _quietTimestamp;

    /// <summary>Whether the overlay has already faded itself away.</summary>
    internal bool Resting { get; set; }

    /// <summary>
    /// A state that is not resting and has just been spoken to, so the rest clock starts now.
    /// </summary>
    internal static RestState Quiet()
    {
        var state = new RestState { Resting = false };
        state._quietTimestamp = global::System.Diagnostics.Stopwatch.GetTimestamp();
        return state;
    }

    /// <summary>
    /// Restarts the quiet clock, which is what any control that wakes the overlay does.
    /// </summary>
    internal void MarkQuiet()
    {
        Resting = false;
        _quietTimestamp = global::System.Diagnostics.Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// How long the overlay has been quiet, in milliseconds.
    /// </summary>
    internal ulong ElapsedMilliseconds()
    {
        var ticks = global::System.Diagnostics.Stopwatch.GetTimestamp() - _quietTimestamp;
        return (ulong)(ticks * 1000L / global::System.Diagnostics.Stopwatch.Frequency);
    }
}
