using AgentDesktop.Core.Response;
using AgentDesktop.Core.Signal;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Signal;

namespace AgentDesktop.Windows.System.Signal;

/// <summary>
/// Assembles the <c>wait --event</c> signal baselines, composing the single-pass windows-and-apps
/// inventory with the app-scoped surface scan under one shared deadline, then deriving completeness
/// from whether each pass actually ran to completion rather than from whether any entity was excluded.
/// Every failure the capture can raise is meant to be narrowed to exactly <c>TIMEOUT</c> or
/// <c>APP_UNRESPONSIVE</c> before it leaves the capture, because core's poll loop aborts the whole wait
/// on any other code instead of retrying.
/// </summary>
internal static class Signals
{
    /// <summary>
    /// Captures one baseline under <paramref name="deadline"/>, starting with the shared deadline
    /// check so an already-spent budget fails before any native enumeration runs.
    /// </summary>
    internal static SignalBaseline CaptureSignalBaseline(SignalFilter filter, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var inventory = CaptureWindowsAndApps(deadline);
        return Assemble(inventory, filter, deadline);
    }

    /// <summary>
    /// The pure-composition half of the capture, separated from the native enumeration so it can be
    /// exercised against a hand-built inventory. It filters the assembled windows and apps and runs the
    /// app-scoped surface scan only when the filter names a process or an app, since core rejects a
    /// surface event without <c>--app</c> and an unscoped scan is therefore never requested. Completeness
    /// for each pass comes from whether that pass ran to completion, never from an unconditional complete
    /// and never from whether any entity was excluded.
    /// </summary>
    internal static SignalBaseline Assemble(SignalWindowInventory inventory, SignalFilter filter, Deadline deadline)
    {
        var filtered = SignalFilterApply.Apply(inventory, filter);
        var scanRequested = filter.Process is not null || filter.App is not null;
        var surfaces = scanRequested
            ? AppScopedSurfaces(filtered.Apps, deadline)
            : new List<SurfaceSignal>();

        return new SignalBaseline(
            filtered.Windows,
            filtered.Apps,
            surfaces,
            new SignalCompleteness(filtered.WindowsComplete, filtered.AppsComplete, scanRequested));
    }

    /// <summary>
    /// SEAM target for the single-pass native windows-and-apps enumeration (Rust
    /// <c>signal_inventory::capture_windows_and_apps</c>). Its failures must be narrowed to
    /// <c>TIMEOUT</c> or <c>APP_UNRESPONSIVE</c>, the Rust <c>narrow_to_permitted_codes</c> step, which
    /// is not yet ported.
    /// </summary>
    private static SignalWindowInventory CaptureWindowsAndApps(Deadline deadline)
    {
        throw AdapterError.NotSupported("wait --event");
    }

    /// <summary>
    /// SEAM target for the app-scoped surface scan (Rust <c>signal_surfaces::app_scoped_surfaces</c>),
    /// with the same narrowing requirement as the inventory. Not yet ported.
    /// </summary>
    private static List<SurfaceSignal> AppScopedSurfaces(List<AppInfo> apps, Deadline deadline)
    {
        throw AdapterError.NotSupported("wait --event");
    }
}
