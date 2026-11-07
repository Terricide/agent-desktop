namespace AgentDesktop.Core.Tree;

/// <summary>Roles that earn a ref. Web runtimes advertise SetFocus, RightClick and ScrollTo
/// on nearly every node, so those actions alone never qualify an element.</summary>
public static class InteractiveRoles
{
    /// <summary>The canonical interactive roles, in the lowercase vocabulary every adapter
    /// emits. Comparison is case-insensitive, so a caller's PascalCase spelling matches.</summary>
    public static readonly IReadOnlySet<string> Names = new HashSet<string>
    {
        "button", "cell", "checkbox", "colorwell", "combobox", "dockitem", "incrementor",
        "link", "listbox", "menubutton", "menuitem", "option", "radiobutton", "slider",
        "switch", "tab", "textfield", "treeitem",
    };

    public static readonly IReadOnlySet<string> NonQualifyingActions = new HashSet<string>
    {
        "AXSetFocus", "SetFocus", "AXRightClick", "RightClick", "AXScrollTo", "ScrollTo",
    };

    public static bool IsInteractive(string role) => Names.Contains(role.Trim().ToLowerInvariant());

    /// <summary>An element earns a ref when its role is interactive, or it advertises a primary
    /// action beyond the three every web runtime advertises.</summary>
    public static bool QualifiesForRef(string role, IReadOnlySet<string> advertisedActions)
    {
        if (IsInteractive(role))
        {
            return true;
        }
        return advertisedActions.Any(action => !NonQualifyingActions.Contains(action));
    }
}
