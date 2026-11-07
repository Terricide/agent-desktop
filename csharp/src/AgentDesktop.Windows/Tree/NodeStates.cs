using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>An element's states, in the order crates/windows/src/tree/states.rs emits them.
/// Each pattern-defined state is read only when its pattern is available. Null means every
/// read-health probe failed, which is unknown rather than an element with no states.</summary>
internal static class NodeStates
{
    private const int StateSystemPressed = 0x0000_0008;

    private const int StateSystemBusy = 0x0000_0800;

    private const int StateSystemHasPopup = 0x4000_0000;

    private const int ToggleStateOn = 1;

    private const int ToggleStateIndeterminate = 2;

    private const int ExpandCollapseStateExpanded = 1;

    private static readonly int[] ReadHealthProbes =
    {
        PropertyIds.IsEnabled,
        PropertyIds.IsOffscreen,
        PropertyIds.HasKeyboardFocus,
        PropertyIds.IsRequiredForForm,
        PropertyIds.IsPassword,
        PropertyIds.LegacyState,
    };

    internal static List<string>? Resolve(NodeProperties properties, string role)
    {
        if (ReadHealthProbes.All(property => properties.Get(property).IsUnknown()))
        {
            return null;
        }

        var states = new List<string>();
        AddWhen(states, properties.Flag(PropertyIds.IsEnabled) == false, "disabled");
        AddWhen(states, properties.Flag(PropertyIds.IsPassword) == true, "secure");
        AddWhen(states, properties.Flag(PropertyIds.IsOffscreen) == true, "offscreen");
        AddWhen(states, properties.Flag(PropertyIds.HasKeyboardFocus) == true, "focused");
        AddWhen(states, properties.Flag(PropertyIds.IsRequiredForForm) == true, "required");
        if (role is "checkbox" or "switch" or "radiobutton")
        {
            var toggle = properties.GatedNumber(PropertyIds.ToggleState);
            AddWhen(states, toggle == ToggleStateOn, "checked");
            AddWhen(states, toggle == ToggleStateIndeterminate, "indeterminate");
        }

        AddWhen(states, properties.GatedNumber(PropertyIds.ExpandCollapseState) == ExpandCollapseStateExpanded, "expanded");
        AddWhen(states, properties.GatedBool(PropertyIds.SelectionItemIsSelected) == true, "selected");
        AddWhen(states, properties.GatedBool(PropertyIds.ValueIsReadOnly) == true, "readonly");
        AddWhen(states, properties.GatedBool(PropertyIds.SelectionCanSelectMultiple) == true, "multiselectable");
        AddWhen(states, properties.GatedBool(PropertyIds.WindowIsModal) == true, "modal");
        if (properties.Number(PropertyIds.LegacyState) is { } bits)
        {
            AddWhen(states, role == "button" && (bits & StateSystemPressed) != 0, "pressed");
            AddWhen(states, (bits & StateSystemHasPopup) != 0, "haspopup");
            AddWhen(states, (bits & StateSystemBusy) != 0, "busy");
        }

        return states;
    }

    private static void AddWhen(List<string> states, bool condition, string state)
    {
        if (condition)
        {
            states.Add(state);
        }
    }
}
