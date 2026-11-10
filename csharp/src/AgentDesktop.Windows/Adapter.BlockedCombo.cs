using AgentDesktop.Core.Input;
using AgentDesktop.Windows.Input;

namespace AgentDesktop.Windows;

public sealed partial class WindowsAdapter
{
    public bool IsBlockedCombo(KeyCombo combo) => BlockedCombo.IsBlocked(combo);
}
