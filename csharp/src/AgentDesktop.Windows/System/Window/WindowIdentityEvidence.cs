using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.System;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows;

/// <summary>
/// The identity evidence a window identifier carries: the owning process, the process generation,
/// and the title captured with it. A window id is accepted only against this evidence, so a handle
/// recycled onto a different process generation is refused before any input reaches it. Port of
/// <c>WindowIdentityEvidence</c> in
/// <c>crates/windows/src/system/window/window_identity.rs</c>.
/// </summary>
internal sealed class WindowIdentityEvidence
{
    /// <summary>The window handle the evidence was captured from.</summary>
    internal nint Handle { get; init; }

    /// <summary>The process that owned the window at capture.</summary>
    internal ProcessId Pid { get; init; }

    /// <summary>The app name captured with the window, the fallback when the process image name is
    /// unreadable.</summary>
    internal string App { get; init; } = "";

    /// <summary>The process generation token captured with the window.</summary>
    internal string ProcessInstance { get; init; } = "";

    /// <summary>The title captured with the window, the one piece of the strict check the OS can
    /// re-answer.</summary>
    internal string? Title { get; init; }

    /// <summary>Builds the evidence from a handle and the window listing that named it, for the app
    /// the command named. A window with no owning process is not evidence, and a window owned by a
    /// different app is not the command's window, so both return nothing.</summary>
    internal static WindowIdentityEvidence? FromInfo(nint handle, WindowInfo info)
    {
        if (info.Pid.Value == 0)
        {
            return null;
        }

        // Rust's `process_instance.as_deref()?` is what makes a window with no process-instance
        // token fail closed here, so the token is required, not defaulted to empty.
        if (string.IsNullOrEmpty(info.ProcessInstance))
        {
            return null;
        }

        return new WindowIdentityEvidence
        {
            Handle = handle,
            Pid = info.Pid,
            App = info.App,
            ProcessInstance = info.ProcessInstance,
            Title = info.Title,
        };
    }

    /// <summary>The full check: the handle is owned by the stored process right now, that process is
    /// the stored generation, the owning thread answers the liveness ping, and the live title
    /// matches the stored one. Every part fails closed, and the pump probe is what makes a hung
    /// window fail here rather than block the command that follows.</summary>
    internal bool VerifyStrict()
    {
        var liveOwner = WindowIdentity.LiveWindowOwner(Handle);
        if (liveOwner != Pid)
        {
            return false;
        }

        if (!ProcessIdentityCheck.MatchesInstance(Pid, ProcessInstance))
        {
            return false;
        }

        if (!WindowEnum.WindowIsResponsive(Handle))
        {
            return false;
        }

        var liveTitle = WindowIdentity.LiveWindowTitle(Handle);
        if (liveTitle is not null && Title is not null)
        {
            return liveTitle.Trim() == Title.Trim();
        }

        return true;
    }

    /// <summary>The check that needs no window server: the stored process is still the generation
    /// captured with it. A recycled pid fails here, which is what a handle recycled onto a new
    /// process generation trips.</summary>
    /// <summary>The stored check: the stored process instance must still be the live generation. Raises
    /// <c>WINDOW_NOT_FOUND</c> naming only the handle, never the title, so a window whose title carries a
    /// marker string cannot leak it through the error. Port of <c>verify_stored</c>, which returns a
    /// result the caller maps.</summary>
    internal void VerifyStored()
    {
        if (!ProcessIdentityCheck.MatchesInstance(Pid, ProcessInstance))
        {
            throw WindowIdentityMismatch(Handle);
        }
    }

    /// <summary>Identity failure that names the HWND and nothing that a window title can carry.</summary>
    private static AdapterError WindowIdentityMismatch(nint handle) =>
        AdapterError.FromCode(
            ErrorCode.WindowNotFound,
            "The window's identity no longer matches its stored evidence")
            .WithSuggestion("Run 'list-windows' to refresh window identifiers, then retry.")
            .WithPlatformDetail($"HWND 0x{handle:X} failed process-instance corroboration");

    /// <summary>Whether the window manager says this exact handle belongs to this exact process
    /// right now. The fact that binds a handle to a pid, rather than merely asserting the pid is
    /// alive.</summary>
    internal bool OwnsHandleNow() => WindowIdentity.LiveWindowOwner(Handle) == Pid;

    /// <summary>Whether the window is a frame the desktop shell hosts for this app: the live owner
    /// is this process, or the shell owns the frame and the shell is the desktop's own
    /// <c>explorer.exe</c>. A shell-hosted frame is the one case where the window's owner is not the
    /// process that supplied its content, and the elevation gate is what keeps an unelevated caller
    /// from claiming a shell frame it cannot read.</summary>
    internal bool HostedFrameOwnsHandle()
    {
        var liveOwner = WindowIdentity.LiveWindowOwner(Handle);
        if (liveOwner == Pid)
        {
            return true;
        }

        if (!Elevation.IsElevated())
        {
            return false;
        }

        return ProcessIdentityCheck.ProcessImageName(liveOwner)
            ?.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) == true;
    }
}
