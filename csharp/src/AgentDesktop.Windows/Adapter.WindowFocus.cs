using AgentDesktop.Core.Context;
using AgentDesktop.Core.Systems;
using AgentDesktop.Windows.System.Window;

namespace AgentDesktop.Windows;

/// <summary>The window focus surface. The activation is the platform's own raise, and it returns
/// only once the target window is confirmed focused, so a window the app keeps in the background
/// fails with the activation refusal rather than reporting a focus the window never took.</summary>
public sealed partial class WindowsAdapter
{
    public void FocusWindow(WindowInfo window, InteractionLease lease) => WindowActivate.FocusWindow(window, lease);
}
