using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>A transparent web wrapper: a Group, Custom or Pane with no name, value or
/// AutomationId that advertises no action other than ScrollTo. Chromium nests these deeply,
/// so under Chromium provenance one costs raw depth but no logical depth. Native stacks are
/// full of the same anonymous shape, which is why the caller gates this on provenance.
/// Mirrors crates/windows/src/tree/walk/wrapper.rs.</summary>
internal static class WebWrapper
{
    internal static bool Matches(NodeProperties properties)
    {
        if (properties.Number(PropertyIds.ControlType) is not (ControlTypeIds.Group or ControlTypeIds.Custom or ControlTypeIds.Pane))
        {
            return false;
        }

        if (properties.NonBlankText(PropertyIds.Name) is not null
            || properties.NonBlankText(PropertyIds.ValueValue) is not null
            || properties.NonBlankText(PropertyIds.AutomationId) is not null)
        {
            return false;
        }

        return AvailableActions.Resolve(properties) is not { } actions
            || actions.TrueForAll(action => action == AvailableActions.ScrollTo);
    }
}
