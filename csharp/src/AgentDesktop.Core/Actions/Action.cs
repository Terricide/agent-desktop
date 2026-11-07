using AgentDesktop.Core.Input;

namespace AgentDesktop.Core.Actions;

/// <summary>The action an element advertises or a command asks an element to perform.</summary>
public enum ActionKind
{
    Click,
    DoubleClick,
    RightClick,
    TripleClick,
    SetValue,
    SetFocus,
    Expand,
    Collapse,
    Select,
    Toggle,
    Check,
    Uncheck,
    Scroll,
    ScrollTo,
    PressKey,
    KeyDown,
    KeyUp,
    TypeText,
    Clear,
    Hover,
    Drag,
}

public sealed record Action
{
    public ActionKind Kind { get; init; }
    public string? Text { get; init; }
    public Direction? Direction { get; init; }
    public uint? Amount { get; init; }
    public KeyCombo? Key { get; init; }
    public DragParams? Drag { get; init; }

    /// <summary>Pointer gestures need the cursor on screen; semantic deliveries need only the window focused.</summary>
    public HeadedRequirement Requirement() => Kind switch
    {
        ActionKind.Click => HeadedRequirement.FocusedWindowAndCursor,
        ActionKind.DoubleClick => HeadedRequirement.FocusedWindowAndCursor,
        ActionKind.RightClick => HeadedRequirement.FocusedWindowAndCursor,
        ActionKind.TripleClick => HeadedRequirement.FocusedWindowAndCursor,
        ActionKind.Scroll => HeadedRequirement.FocusedWindowAndCursor,
        ActionKind.Hover => HeadedRequirement.FocusedWindowAndCursor,
        ActionKind.Drag => HeadedRequirement.FocusedWindowAndCursor,
        _ => HeadedRequirement.FocusedWindow,
    };

    public string Name() => Kind switch
    {
        ActionKind.Click => "click",
        ActionKind.DoubleClick => "double-click",
        ActionKind.RightClick => "right-click",
        ActionKind.TripleClick => "triple-click",
        ActionKind.SetValue => "set-value",
        ActionKind.SetFocus => "focus",
        ActionKind.Expand => "expand",
        ActionKind.Collapse => "collapse",
        ActionKind.Select => "select",
        ActionKind.Toggle => "toggle",
        ActionKind.Check => "check",
        ActionKind.Uncheck => "uncheck",
        ActionKind.Scroll => "scroll",
        ActionKind.ScrollTo => "scroll-to",
        ActionKind.PressKey => "press-key",
        ActionKind.KeyDown => "key-down",
        ActionKind.KeyUp => "key-up",
        ActionKind.TypeText => "type",
        ActionKind.Clear => "clear",
        ActionKind.Hover => "hover",
        ActionKind.Drag => "drag",
        _ => "internal",
    };
}
