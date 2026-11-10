using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows;

/// <summary>
/// The live reads a window's stored identity is corroborated against: the process that owns a handle
/// right now, the title a live window answers, and the fail-closed refusal when the stored evidence
/// no longer matches the handle. Port of the free functions in
/// <c>crates/windows/src/system/window/window_identity.rs</c>.
/// </summary>
internal static class WindowIdentity
{
    /// <summary>How long a window gets to answer the liveness ping before its title is treated as
    /// unreadable. The title is best-effort evidence, so a window that will not answer simply has
    /// no readable title, and identity still rests on the owner and generation checks that never
    /// touch the message queue.</summary>
    private const uint TitleProbeMs = 250;

    /// <summary>Reads the process that owns a window handle right now, the fact that binds a handle
    /// to a pid rather than merely asserting the pid is alive. <c>null</c> is every kind of
    /// unanswerable: a destroyed handle, a handle the window manager no longer knows, and the lane
    /// where no window server is reachable. Callers treat it as a failed match, never as an absent
    /// constraint, so an unreadable owner fails closed. The pid out-parameter decides the answer
    /// rather than the returned thread id: both are zero exactly when the call fails, since an
    /// invalid handle leaves the pre-zeroed variable untouched and a live window belongs to a
    /// process whose id is never zero.</summary>
    internal static ProcessId? LiveWindowOwner(nint handle) =>
        WindowEnum.LiveWindowOwner(handle) is { } pid ? ProcessId.From(pid) : null;

    /// <summary>Reads the live title of a window handle, the one piece of the strict check only the
    /// OS can answer for. The pump probe runs first: a same-process window whose thread never
    /// dispatches blocks the read outright, so a window that will not answer has no readable title.
    /// An empty answer is the absent title.</summary>
    internal static string? LiveWindowTitle(nint handle)
    {
        var title = WindowEnum.LiveWindowTitle(handle, TitleProbeMs);
        return title.Length == 0 ? null : title;
    }

    /// <summary>The process image name, or an empty string when the process table does not name the
    /// pid. An unreadable name never matches a stored application name, so the strict check fails
    /// closed on it.</summary>
    internal static string ProcessImageNameOrEmpty(ProcessId pid) =>
        ProcessSnapshot.Capture().ImageNameFor(pid.Value) ?? "";

    /// <summary>The fail-closed identity-mismatch error, carrying no window-derived text: the handle
    /// is named and the title never is, so a marker carried in a window title cannot leak into the
    /// error shape.</summary>
    internal static AdapterError WindowIdentityMismatch(nint handle) =>
        new AdapterError(
                ErrorCode.WindowNotFound,
                "The window's identity no longer matches its stored evidence")
            .WithSuggestion("Run 'list-windows' to refresh window identifiers, then retry.")
            .WithPlatformDetail(
                $"HWND 0x{unchecked((ulong)(long)handle):X} failed process-instance corroboration");
}
