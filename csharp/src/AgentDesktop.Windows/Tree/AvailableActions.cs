using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>The actions an element advertises, from its pattern availability, in the order
/// crates/windows/src/tree/actions.rs emits them and without duplicates. Null means the
/// whole affordance read set failed, which is unknown rather than an element with none.</summary>
internal static class AvailableActions
{
    internal const string Click = "Click";

    internal const string ScrollTo = "ScrollTo";

    private static readonly TreeProperty[] Affordances =
    {
        TreeProperty.InvokeAvailable,
        TreeProperty.ToggleAvailable,
        TreeProperty.ExpandCollapseAvailable,
        TreeProperty.SelectionItemAvailable,
        TreeProperty.ValueAvailable,
        TreeProperty.RangeValueAvailable,
        TreeProperty.ScrollAvailable,
        TreeProperty.ScrollItemAvailable,
    };

    internal static List<string>? Resolve(ElementProperties properties)
    {
        if (ReadSetIsUnknown(properties))
        {
            return null;
        }

        var actions = new List<string>();
        AddWhen(actions, properties.IsTrue(TreeProperty.InvokeAvailable), Click);
        AddWhen(actions, properties.IsTrue(TreeProperty.ToggleAvailable), "Toggle");
        AddWhen(actions, properties.IsTrue(TreeProperty.ExpandCollapseAvailable), "Expand");
        AddWhen(actions, properties.IsTrue(TreeProperty.ExpandCollapseAvailable), "Collapse");
        AddWhen(actions, properties.IsTrue(TreeProperty.SelectionItemAvailable), "Select");
        AddWhen(actions, IsWritable(properties, TreeProperty.ValueAvailable, TreeProperty.ValueIsReadOnly), "SetValue");
        AddWhen(actions, IsWritable(properties, TreeProperty.RangeValueAvailable, TreeProperty.RangeValueIsReadOnly), "SetValue");
        AddWhen(actions, properties.IsTrue(TreeProperty.ScrollAvailable), "Scroll");
        AddWhen(actions, properties.IsTrue(TreeProperty.ScrollItemAvailable), ScrollTo);
        AddWhen(actions, properties.NonBlankText(TreeProperty.LegacyDefaultAction) is not null, Click);
        AddWhen(actions, properties.IsTrue(TreeProperty.IsKeyboardFocusable), "SetFocus");
        return actions;
    }

    private static bool IsWritable(ElementProperties properties, TreeProperty available, TreeProperty readOnly) =>
        properties.IsTrue(available) && properties.GatedBool(readOnly) == false;

    private static bool ReadSetIsUnknown(ElementProperties properties) =>
        Affordances.All(property => properties.Get(property).IsUnknown())
        && properties.Get(TreeProperty.LegacyDefaultAction).IsUnknown()
        && properties.Get(TreeProperty.IsKeyboardFocusable).IsUnknown();

    private static void AddWhen(List<string> actions, bool condition, string action)
    {
        if (condition && !actions.Contains(action))
        {
            actions.Add(action);
        }
    }
}
