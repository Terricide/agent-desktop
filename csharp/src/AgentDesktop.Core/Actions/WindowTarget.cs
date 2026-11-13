using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Actions;

/// <summary>Builds the target a window operation reports. The target names the window the adapter
/// validated, and the process instance token is part of it: without the token the report cannot name
/// which running copy of the app the write reached, so the operation refuses before the window is
/// written to rather than reporting a delivery that names the wrong process. Port of
/// <c>OperationTarget::for_window</c> in <c>crates/core/src/system/operation_target.rs</c>.</summary>
public static class WindowTarget
{
    public static OperationTarget ForWindow(WindowInfo window)
    {
        if (string.IsNullOrEmpty(window.ProcessInstance))
        {
            throw AdapterError.Internal(
                    "The window has no process instance token to report as the target")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return new OperationTarget
        {
            Kind = "window",
            Window = new OperationWindow(
                window.Id,
                window.Title,
                new ProcessIdentity(window.Pid, window.ProcessInstance)),
        };
    }
}
