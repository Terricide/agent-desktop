using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;

namespace AgentDesktop.Windows;

public sealed partial class WindowsAdapter
{
    public PermissionReport PermissionReport(Deadline deadline) => Permissions.Report(deadline);

    public PermissionReport RequestPermissions(InteractionLease lease) =>
        Permissions.RequestReport(lease.Deadline());
}
