using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Systems;
using AgentDesktop.Windows.System;
using AgentDesktop.Windows.System.Window;

namespace AgentDesktop.Windows;

/// <summary>The window lifecycle surface: minimize, maximize, restore, resize, and move. Each write is
/// proved by a placement re-read in <see cref="WindowOpExecute"/>; the report names the read-back scope
/// the adapter verified, so a write that landed inside its tolerance is delivered-verified and one that
/// was accepted but never read back is delivered-unverified.</summary>
public sealed partial class WindowsAdapter
{
    public OperationReport WindowOperation(WindowInfo window, WindowOp op, InteractionLease lease)
    {
        var target = WindowTarget.ForWindow(window);
        WindowOpExecute.Execute(window, op, lease.Deadline());
        return OperationReportBuild.WindowOperation(target, op, WindowOpReadback.SelectReadback(op));
    }
}
