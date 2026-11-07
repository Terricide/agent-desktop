using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>The actions an element advertises, from its pattern availability, in the order
/// crates/windows/src/tree/actions.rs emits them and without duplicates. Null means the
/// whole affordance read set failed, which is unknown rather than an element with none.</summary>
internal static class AvailableActions
{
    internal const string Click = "Click";

    internal const string ScrollTo = "ScrollTo";

    private static readonly int[] Affordances =
    {
        PropertyIds.IsInvokePatternAvailable,
        PropertyIds.IsTogglePatternAvailable,
        PropertyIds.IsExpandCollapsePatternAvailable,
        PropertyIds.IsSelectionItemPatternAvailable,
        PropertyIds.IsValuePatternAvailable,
        PropertyIds.IsRangeValuePatternAvailable,
        PropertyIds.IsScrollPatternAvailable,
        PropertyIds.IsScrollItemPatternAvailable,
    };

    internal static List<string>? Resolve(NodeProperties properties)
    {
        if (ReadSetIsUnknown(properties))
        {
            return null;
        }

        var actions = new List<string>();
        AddWhen(actions, properties.IsTrue(PropertyIds.IsInvokePatternAvailable), Click);
        AddWhen(actions, properties.IsTrue(PropertyIds.IsTogglePatternAvailable), "Toggle");
        AddWhen(actions, properties.IsTrue(PropertyIds.IsExpandCollapsePatternAvailable), "Expand");
        AddWhen(actions, properties.IsTrue(PropertyIds.IsExpandCollapsePatternAvailable), "Collapse");
        AddWhen(actions, properties.IsTrue(PropertyIds.IsSelectionItemPatternAvailable), "Select");
        AddWhen(actions, IsWritable(properties, PropertyIds.IsValuePatternAvailable, PropertyIds.ValueIsReadOnly), "SetValue");
        AddWhen(actions, IsWritable(properties, PropertyIds.IsRangeValuePatternAvailable, PropertyIds.RangeValueIsReadOnly), "SetValue");
        AddWhen(actions, properties.IsTrue(PropertyIds.IsScrollPatternAvailable), "Scroll");
        AddWhen(actions, properties.IsTrue(PropertyIds.IsScrollItemPatternAvailable), ScrollTo);
        AddWhen(actions, properties.NonBlankText(PropertyIds.LegacyDefaultAction) is not null, Click);
        AddWhen(actions, properties.IsTrue(PropertyIds.IsKeyboardFocusable), "SetFocus");
        return actions;
    }

    private static bool IsWritable(NodeProperties properties, int available, int readOnly) =>
        properties.IsTrue(available) && properties.GatedBool(readOnly) == false;

    private static bool ReadSetIsUnknown(NodeProperties properties) =>
        Affordances.All(property => properties.Get(property).IsUnknown())
        && properties.Get(PropertyIds.LegacyDefaultAction).IsUnknown()
        && properties.Get(PropertyIds.IsKeyboardFocusable).IsUnknown();

    private static void AddWhen(List<string> actions, bool condition, string action)
    {
        if (condition && !actions.Contains(action))
        {
            actions.Add(action);
        }
    }
}
