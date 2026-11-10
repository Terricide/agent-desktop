using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>A transparent web wrapper: a Group, Custom or Pane with no name, value or
/// AutomationId that advertises no action other than ScrollTo. Chromium nests these deeply,
/// so under Chromium provenance one costs raw depth but no logical depth. Native stacks are
/// full of the same anonymous shape, which is why the caller gates this on provenance.
/// Mirrors crates/windows/src/tree/walk/wrapper.rs.</summary>
internal static class WebWrapper
{
    internal static bool Matches(ElementProperties properties)
    {
        if (properties.Number(TreeProperty.ControlType) is not (ControlTypeIds.Group or ControlTypeIds.Custom or ControlTypeIds.Pane))
        {
            return false;
        }

        if (properties.NonBlankText(TreeProperty.Name) is not null
            || properties.NonBlankText(TreeProperty.Value) is not null
            || properties.NonBlankText(TreeProperty.AutomationId) is not null)
        {
            return false;
        }

        return AvailableActions.Resolve(properties) is not { } actions
            || actions.TrueForAll(action => action == AvailableActions.ScrollTo);
    }
}
