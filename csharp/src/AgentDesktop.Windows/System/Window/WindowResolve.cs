using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Window;

/// <summary>
/// Resolves a stored <see cref="WindowInfo"/> against the live desktop: the handle the stored id
/// names, its current owner, and the top-level inventory the handle must still appear in.
/// </summary>
internal static class WindowResolve
{
    /// <summary>
    /// Resolves a live window by <see cref="WindowInfo.Id"/>, corroborating pid and process
    /// generation against the handle's current owner (the stored-evidence rule). A stored window
    /// whose handle no longer addresses a live top-level window, or whose owner is not the stored
    /// process instance, fails closed: it never resolves into whatever the window manager handed
    /// the recycled handle to next.
    /// </summary>
    internal static WindowInfo ResolveWindowStrict(WindowInfo expected, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var handle = WindowOps.ParseHandle(expected.Id);
        if (handle == nint.Zero)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "window id is not a Windows HWND-shaped id");
        }

        if (!WindowExists(handle))
        {
            throw new AdapterError(ErrorCode.WindowNotFound, "window handle is no longer a live top-level window");
        }

        var evidence = WindowIdentityEvidence.FromInfo(handle, expected);
        if (evidence is null)
        {
            throw new AdapterError(
                ErrorCode.StaleRef,
                "headed focus requires a process-instance token on the stored window");
        }

        evidence.VerifyStored();
        return LiveWindowInfo(handle, expected);
    }

    /// <summary>
    /// Rebuilds the stored window from the live inventory entry that carries its handle: the live
    /// title where it can be read, the stored app, pid and generation the evidence already
    /// corroborated, the geometry the enumeration pass measured, and the state the foreground and
    /// show-status reads proved. A handle that the top-level walk no longer yields, or that fails
    /// the census filter without being minimized, is not an agent-visible window and fails closed.
    /// </summary>
    private static WindowInfo LiveWindowInfo(nint handle, WindowInfo expected)
    {
        EnumeratedWindow? found = null;
        WindowEnum.EnumerateTopLevel(candidate =>
        {
            if (candidate.Handle != handle)
            {
                return true;
            }

            found = candidate;
            return false;
        });

        if (found is null)
        {
            throw new AdapterError(
                ErrorCode.WindowNotFound,
                "window handle is no longer present in the top-level inventory");
        }

        var window = found.Value;
        if (!WindowOps.PassesFilter(window) && !window.Iconic)
        {
            throw new AdapterError(
                ErrorCode.WindowNotFound,
                "window is no longer an agent-visible top-level window");
        }

        var focused = WindowOps.IsForegroundWindow(handle);
        return new WindowInfo
        {
            Id = expected.Id,
            Title = WindowIdentity.LiveWindowTitle(handle) ?? expected.Title,
            App = expected.App,
            Pid = expected.Pid,
            ProcessInstance = expected.ProcessInstance,
            Bounds = window.Rect,
            State = WindowOps.BuildWindowState(focused, window.Iconic, window.Visible),
        };
    }

    /// <summary>
    /// Whether a handle is a live top-level window right now. Off Windows there is no window
    /// server to answer, so the honest answer is that no handle is live and every resolution
    /// fails closed.
    /// </summary>
    private static bool WindowExists(nint handle) =>
        global::System.OperatingSystem.IsWindows() && IsWindow(handle) != 0;

    [DllImport("user32.dll")]
    private static extern int IsWindow(nint hWnd);
}
