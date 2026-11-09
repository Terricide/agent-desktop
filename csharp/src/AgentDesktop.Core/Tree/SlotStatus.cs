namespace AgentDesktop.Core.Tree;

/// <summary>Whether one name-evidence slot's value can be trusted, and whether it applies to
/// this element at all.</summary>
public enum SlotStatus
{
    /// <summary>The slot was read and its value is what it says.</summary>
    Certain,

    /// <summary>The read failed. The slot contributes uncertainty to every weaker source,
    /// because the value that would have won the precedence may be the one that is missing.</summary>
    Uncertain,

    /// <summary>The slot does not apply to this element on this platform, and contributes
    /// neither a value nor uncertainty. This is how a platform folds its own role gating in
    /// before calling the shared precedence, instead of passing a platform role token into
    /// shared code.</summary>
    Suppressed,
}
