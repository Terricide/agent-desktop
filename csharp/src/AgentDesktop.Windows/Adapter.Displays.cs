using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;

namespace AgentDesktop.Windows;

public sealed partial class WindowsAdapter
{
    public List<DisplayInfo> ListDisplays(Deadline deadline) => Display.ListDisplaysLive(deadline);
}
