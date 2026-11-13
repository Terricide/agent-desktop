using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Windows.Input;

namespace AgentDesktop.Windows;

public sealed partial class WindowsAdapter
{
    public bool IsBlockedCombo(KeyCombo combo) => BlockedCombo.IsBlocked(combo);

    public OperationReport MouseEvent(MouseEvent mouseEvent, InteractionLease lease) =>
        AdapterMouseOps.MouseEvent(mouseEvent, lease);
}
