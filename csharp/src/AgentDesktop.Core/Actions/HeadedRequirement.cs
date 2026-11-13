namespace AgentDesktop.Core.Actions;

/// <summary>Whether a delivery needs a headed session: the window focused, and the cursor on screen too.</summary>
public enum HeadedRequirement
{
    None,
    FocusedWindow,
    FocusedWindowAndCursor,
}
