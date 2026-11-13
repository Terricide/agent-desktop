using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Windows.Input;

namespace AgentDesktop.Windows;

public sealed partial class WindowsAdapter
{
    public OperationReport Drag(DragParams parameters, InteractionLease lease)
    {
        Input.Drag.SynthesizeDrag(parameters, lease.Deadline());
        return OperationReportBuild.DragDispatched(parameters);
    }
}
