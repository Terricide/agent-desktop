using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input.Clipboard;

namespace AgentDesktop.Windows;

public sealed partial class WindowsAdapter
{
    public OperationReport ClearClipboard(InteractionLease lease)
    {
        ClipboardWrite.ClearClipboard(lease.Deadline());
        return OperationReport.DeliveredUnverified("clipboard-clear", new OperationTarget { Kind = "clipboard" });
    }

    public OperationReport SetClipboardContent(ClipboardContent content, InteractionLease lease)
    {
        ClipboardWrite.SetClipboardContent(content, lease.Deadline());
        return OperationReport.DeliveredUnverified("clipboard-set", new OperationTarget { Kind = "clipboard" });
    }
}
