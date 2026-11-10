namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// What the cursor is doing in a presentation, which decides the reveal, the
/// trail, and whether a caret follow needs a target.
/// </summary>
public enum CursorPhase
{
    /// The cursor moves to the destination along the arc.
    Travel,

    /// The cursor carries a press from a start point to the destination.
    Drag,

    /// The cursor holds at the destination and the effect ring expands.
    Effect,

    /// The cursor is a caret that appears at the destination.
    Caret,

    /// The caret moves from its previous target to the new one.
    CaretFollow,
}
