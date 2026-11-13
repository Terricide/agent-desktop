namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// The time budgets the cursor overlay moves within, in milliseconds.
/// </summary>
public static class CursorOverlayTiming
{
    /// How long a platform renderer may take to report that the cursor reached
    /// its destination before the action dispatches anyway.
    public const ulong ArrivalTimeoutMs = 900;

    /// The largest cursor travel plus dwell that still leaves room for the
    /// socket round trip and frame pacing inside the arrival timeout.
    public const ulong MotionBudgetMs = ArrivalTimeoutMs - 200;

    /// How long the overlay stays visible with no instruction before it rests.
    public const ulong IdleRestMs = 6000;

    /// How long the outline around a clicked element stays on screen.
    public const ulong HighlightHoldMs = 900;

    /// How long the overlay takes to fade out once it rests.
    public const ulong RestFadeMs = 156;

    /// How long the label card takes to appear once its text changes.
    public const ulong LabelRevealMs = 180;
}
