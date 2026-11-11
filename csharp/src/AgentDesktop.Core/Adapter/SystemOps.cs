using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Adapter;

/// <summary>App lifecycle, windows, permissions, and screenshots. Every method defaults to a refusal. The full surface is ported in the Windows and macOS stages; this is the subset the contract shape needs.</summary>
public interface SystemOps
{
    /// <summary>An adapter that never overrides this has not probed permissions at all, which is not the same fact as a user denying one. Unknown routes to PLATFORM_NOT_SUPPORTED rather than the misleading PERM_DENIED.</summary>
    PermissionReport PermissionReport(Deadline deadline) => new()
    {
        Accessibility = PermissionState.Unknown,
        ScreenRecording = PermissionState.Unknown,
        Automation = PermissionState.NotRequired,
    };

    bool UnknownAccessibilityMeansUnsupported() => true;

    InteractionLease AcquireInteractionLease(Deadline deadline) =>
        throw AdapterError.NotSupported("acquire_interaction_lease");

    /// <summary>Performs one renderer-accessibility activation mutation and returns. Readiness polling belongs to core after the lease is dropped.</summary>
    void ActivateRendererAccessibility(ProcessIdentity process, InteractionLease lease) =>
        throw AdapterError.NotSupported("activate_renderer_accessibility");

    PermissionReport RequestPermissions(InteractionLease lease) => PermissionReport(lease.Deadline());

    /// <summary>Performs the platform-native focus/raise for the exact window. Implementations return only after that window is confirmed focused.</summary>
    void FocusWindow(WindowInfo window, InteractionLease lease) =>
        throw AdapterError.NotSupported("focus_window");

    void FocusApp(ProcessIdentity process, InteractionLease lease) =>
        throw AdapterError.NotSupported("focus_app");

    bool ProcessOwnsTcpListener(ProcessIdentity process, ushort port, Deadline deadline) =>
        throw AdapterError.NotSupported("process_owns_tcp_listener");

    /// <summary>Reads the active monitors. The enumeration is a read, so it needs no lease.</summary>
    List<DisplayInfo> ListDisplays(Deadline deadline) =>
        throw AdapterError.NotSupported("list_displays");
}
