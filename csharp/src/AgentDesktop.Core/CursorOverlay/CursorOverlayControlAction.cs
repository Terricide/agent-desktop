namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// What a control asks the renderer to do. The wire names are the lowercase
/// words the Rust renderer matches on.
/// </summary>
public enum CursorOverlayControlAction
{
    /// Turn the overlay on for this agent, with a greeting.
    Enable,

    /// Show one presentation: travel, drag, effect, caret, or caret follow.
    Present,

    /// Hide this agent's overlay.
    Hide,

    /// Show this agent's overlay again.
    Show,

    /// Turn this agent's overlay off.
    Disable,
}
