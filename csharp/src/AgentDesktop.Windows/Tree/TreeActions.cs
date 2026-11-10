using AgentDesktop.Core.Locator;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// Resolves the available-action list for one element from its property read set, in the order the
/// pattern availability implies it, without duplicates.
/// </summary>
/// <remarks>
/// An action is emitted only for a pattern whose availability implies a specific affordance:
/// <c>Invoke</c>, <c>Toggle</c>, <c>ExpandCollapse</c>, <c>SelectionItem</c>, <c>Value</c> gated on
/// not read-only, <c>RangeValue</c> gated on not read-only, <c>Scroll</c>, <c>ScrollItem</c>.
/// <c>LegacyIAccessible</c> availability is not one of those: A2-2 measured it true on 141 of 141
/// elements, so treating it as an affordance would ref-allocate every element in every tree. It
/// contributes exactly one action, <c>Click</c>, and only when <c>LegacyDefaultAction</c> itself
/// reads as a non-empty string - the gate measured working: button, checkbox and list item
/// non-empty; edit, text and container roles empty. <c>SetFocus</c> is emitted truthfully from
/// <c>IsKeyboardFocusable</c>, but core already declines to treat a bare <c>SetFocus</c> as a
/// primary action, so it can never be the sole reason an element becomes ref-able.
/// The field is <c>Known</c> once the read set succeeded, including when the list is empty - that
/// is the legitimate answer for an inert element. It is <c>Unknown</c> only when every affordance
/// availability property, <c>LegacyDefaultAction</c> and <c>IsKeyboardFocusable</c> read unknown:
/// nothing was learned about this element's affordances, so there is nothing to report as known. A
/// single failed property among otherwise-successful reads does not void the whole answer - that
/// property simply contributes no affordance, the same way an unread pattern would on a provider
/// that never implemented it.
/// </remarks>
internal static class TreeActions
{
    private const string Click = "Click";
    private const string Toggle = "Toggle";
    private const string Expand = "Expand";
    private const string Collapse = "Collapse";
    private const string Select = "Select";
    private const string SetValue = "SetValue";
    private const string Scroll = "Scroll";
    private const string ScrollTo = "ScrollTo";
    private const string SetFocus = "SetFocus";

    /// <summary>
    /// The pattern-availability properties this module reads to decide whether the read set as a
    /// whole succeeded. <c>LegacyAvailable</c> is deliberately absent from this list. A2-2 measured
    /// it true on 141 of 141 elements, so its own read succeeding proves nothing about any real
    /// affordance; counting it toward whether the reads succeeded would let a tree where every
    /// other pattern read failed still report known, which is the same failure shape the rule
    /// below forbids for every other property: an action implied by a read that did not actually
    /// succeed.
    /// </summary>
    private static readonly TreeProperty[] AffordanceAvailability =
    [
        TreeProperty.InvokeAvailable,
        TreeProperty.ToggleAvailable,
        TreeProperty.ExpandCollapseAvailable,
        TreeProperty.SelectionItemAvailable,
        TreeProperty.ValueAvailable,
        TreeProperty.RangeValueAvailable,
        TreeProperty.ScrollAvailable,
        TreeProperty.ScrollItemAvailable,
    ];

    /// <summary>
    /// Resolves the available-action list from the read set, as known, absent-never, or unknown.
    /// </summary>
    internal static LocatorField<List<string>> ResolveActions(ElementProperties properties)
    {
        if (ReadSetIsUnknown(properties))
        {
            return LocatorField<List<string>>.Unknown();
        }

        var actions = new List<string>();

        if (properties.IsTrue(TreeProperty.InvokeAvailable))
        {
            PushUnique(actions, Click);
        }

        if (properties.IsTrue(TreeProperty.ToggleAvailable))
        {
            PushUnique(actions, Toggle);
        }

        if (properties.IsTrue(TreeProperty.ExpandCollapseAvailable))
        {
            PushUnique(actions, Expand);
            PushUnique(actions, Collapse);
        }

        if (properties.IsTrue(TreeProperty.SelectionItemAvailable))
        {
            PushUnique(actions, Select);
        }

        if (properties.IsTrue(TreeProperty.ValueAvailable)
            && properties.GatedBool(TreeProperty.ValueIsReadOnly) == false)
        {
            PushUnique(actions, SetValue);
        }

        if (properties.IsTrue(TreeProperty.RangeValueAvailable)
            && properties.GatedBool(TreeProperty.RangeValueIsReadOnly) == false)
        {
            PushUnique(actions, SetValue);
        }

        if (properties.IsTrue(TreeProperty.ScrollAvailable))
        {
            PushUnique(actions, Scroll);
        }

        if (properties.IsTrue(TreeProperty.ScrollItemAvailable))
        {
            PushUnique(actions, ScrollTo);
        }

        if (HasLegacyDefaultAction(properties))
        {
            PushUnique(actions, Click);
        }

        if (properties.IsTrue(TreeProperty.IsKeyboardFocusable))
        {
            PushUnique(actions, SetFocus);
        }

        return LocatorField<List<string>>.Known(actions);
    }

    private static bool ReadSetIsUnknown(ElementProperties properties) =>
        AffordanceAvailability.All(property => properties.Get(property).IsUnknown())
        && properties.Get(TreeProperty.LegacyDefaultAction).IsUnknown()
        && properties.Get(TreeProperty.IsKeyboardFocusable).IsUnknown();

    private static bool HasLegacyDefaultAction(ElementProperties properties) =>
        properties.Get(TreeProperty.LegacyDefaultAction).NonBlankText() is not null;

    private static void PushUnique(List<string> actions, string action)
    {
        if (!actions.Contains(action))
        {
            actions.Add(action);
        }
    }
}
