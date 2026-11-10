using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows.Signal;

/// <summary>One coherent instant's windows and apps. The completeness flags report whether the walk ran to completion, never whether every entity was identifiable.</summary>
internal sealed record SignalWindowInventory(
    List<WindowInfo> Windows,
    List<AppInfo> Apps,
    bool WindowsComplete,
    bool AppsComplete)
{
    internal static SignalWindowInventory Truncated() => new([], [], false, false);
}
