using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Closes one application by exact process identity. A close is only attempted against the process
/// instance token the caller observed, so a recycled pid is never signalled.
/// </summary>
internal static class Close
{
    private const int ExitPollMs = 25;
    private const int ProcessTerminate = 0x0001;
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessSynchronize = 0x00100000;
    private const uint WaitObject0 = 0;
    private const uint WmClose = 0x0010;
    private const ulong TicksPerSecond = 10_000_000UL;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int PostMessageW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetExitCodeProcess(nint process, out uint exitCode);

    /// <summary>
    /// Closes an application after the protected-process and instance-token checks. Throws
    /// <see cref="AdapterError"/> with a delivery disposition on every failure.
    /// </summary>
    internal static CloseOutcome CloseApp(AppInfo app, bool force, Deadline deadline)
    {
        EnsureNotProtected(app.Name);
        if (app.BundleId is not null)
        {
            EnsureNotProtected(app.BundleId);
        }

        var instance = app.ProcessInstance
            ?? throw new AdapterError(ErrorCode.InvalidArgs, "Exact close requires a process instance token")
                .WithDisposition(DeliverySemantics.NotDelivered());
        return CloseVerified(app, instance, force, deadline);
    }

    private static CloseOutcome CloseVerified(AppInfo app, string instance, bool force, Deadline deadline)
    {
        RunBefore(() => deadline.EnsureRemaining());
        if (ProcessObservedGone(app.Pid, instance))
        {
            return CloseOutcome.AlreadyGone;
        }

        var dispatch = force
            ? ForceTerminate(app.Pid, instance, deadline)
            : GracefulClose(app.Pid, instance, deadline);
        if (dispatch == CloseDispatch.AlreadyGone)
        {
            return CloseOutcome.AlreadyGone;
        }

        WaitForExit(app.Name, app.Pid, instance, force, deadline);
        return CloseOutcome.Exited;
    }

    private static void EnsureNotProtected(string id)
    {
        if (AppOps.IsProtectedProcess(id))
        {
            throw new AdapterError(ErrorCode.InvalidArgs, $"'{id}' is a protected system process and cannot be closed")
                .WithSuggestion("Target a regular application; session-critical processes are never closed.");
        }
    }

    private static CloseDispatch GracefulClose(ProcessId pid, string instance, Deadline deadline)
    {
        RunBefore(() => deadline.EnsureRemaining());
        var windows = ReadBefore(() => TopLevelWindowsForPid(pid));
        if (windows.Count == 0)
        {
            return WindowlessGracefulFallback(pid, instance, deadline);
        }

        var delivered = BroadcastClose(windows, deadline, hwnd => PostWmCloseIfStillOwned(hwnd, pid, instance));
        return delivered ? CloseDispatch.Sent : WindowlessGracefulFallback(pid, instance, deadline);
    }

    /// <summary>
    /// Posts the close request to every owned window and reports whether any window received it. One
    /// window's refusal does not cancel delivery to its siblings, and a window destroyed between enumeration
    /// and post does not fail the fan-out. A failure surfaces only when no post succeeded and at least one
    /// post failed. A deadline that expires after a successful post stops the loop instead of failing it,
    /// because a delivered request may already be closing the app. Ownership that changed for every window
    /// posts nothing and reports false, so the caller applies its liveness check rather than assume delivery.
    /// </summary>
    private static bool BroadcastClose(IReadOnlyList<nint> windows, Deadline deadline, Func<nint, bool> post)
    {
        var delivered = false;
        AdapterError? firstFailure = null;
        foreach (var hwnd in windows)
        {
            if (deadline.IsExpired())
            {
                if (delivered)
                {
                    break;
                }

                throw deadline.TimeoutError().WithDisposition(DeliverySemantics.NotDelivered());
            }

            try
            {
                if (post(hwnd))
                {
                    delivered = true;
                }
            }
            catch (AdapterError error)
            {
                firstFailure ??= error;
            }
        }

        if (firstFailure is not null && !delivered)
        {
            throw firstFailure.WithDisposition(DeliverySemantics.NotDelivered());
        }

        return delivered;
    }

    /// <summary>
    /// The honest verdict when no close request could be delivered. Nothing was sent, so a process that has
    /// since exited is the benign success and a live one is a failure that suggests a forced termination.
    /// </summary>
    private static CloseDispatch WindowlessGracefulFallback(ProcessId pid, string instance, Deadline deadline)
    {
        RunBefore(() => deadline.EnsureRemaining());
        if (ProcessObservedGone(pid, instance))
        {
            return CloseDispatch.AlreadyGone;
        }

        throw new AdapterError(ErrorCode.ActionFailed, $"No window owned by process {pid.Value} could receive WM_CLOSE")
            .WithDetails(new JsonObject { ["pid"] = pid.Value })
            .WithSuggestion($"Retry with --force to terminate pid {pid.Value} without WM_CLOSE")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }

    private static CloseDispatch ForceTerminate(ProcessId pid, string instance, Deadline deadline)
    {
        RunBefore(() => deadline.EnsureRemaining());
        if (ProcessObservedGone(pid, instance))
        {
            return CloseDispatch.AlreadyGone;
        }

        const int access = ProcessTerminate | ProcessQueryLimitedInformation | ProcessSynchronize;
        var handle = Interop.OpenProcess(access, false, pid.Value);
        if (handle == nint.Zero)
        {
            var openError = (uint)Marshal.GetLastPInvokeError();
            if (ProcessObservedGone(pid, instance))
            {
                return CloseDispatch.AlreadyGone;
            }

            throw BeforeTermination(HResult.AdapterErrorFromWin32(openError, "OpenProcess failed before TerminateProcess"));
        }

        bool tokenMatches;
        try
        {
            tokenMatches = HandleMatchesInstance(handle, instance);
        }
        catch (AdapterError error)
        {
            Interop.CloseHandle(handle);
            throw BeforeTermination(error);
        }

        if (!tokenMatches)
        {
            Interop.CloseHandle(handle);
            return CloseDispatch.AlreadyGone;
        }

        var terminated = TerminateProcess(handle, 1) != 0;
        var terminateError = terminated ? 0u : (uint)Marshal.GetLastPInvokeError();
        Interop.CloseHandle(handle);

        if (!terminated)
        {
            if (ProcessObservedGone(pid, instance))
            {
                return CloseDispatch.AlreadyGone;
            }

            throw BeforeTermination(HResult.AdapterErrorFromWin32(terminateError, "TerminateProcess failed"));
        }

        return CloseDispatch.Sent;
    }

    private static void WaitForExit(string id, ProcessId pid, string instance, bool force, Deadline deadline)
    {
        while (true)
        {
            if (ProcessObservedGone(pid, instance))
            {
                return;
            }

            if (deadline.IsExpired())
            {
                throw deadline.TimeoutError()
                    .WithDetails(new JsonObject { ["app"] = id, ["pid"] = pid.Value, ["force"] = force })
                    .WithDisposition(DeliverySemantics.DeliveredUnverified());
            }

            RunAfter(() => deadline.EnsureRemaining());
            var pauseMs = global::System.Math.Min(ExitPollMs, deadline.RemainingMs());
            global::System.Threading.Thread.Sleep((int)pauseMs);
        }
    }

    /// <summary>
    /// Wait-gated exit observation: a signalled process handle, or a creation-time token that no longer
    /// matches a live pid. The wait decides, never the exit code, because a process may legitimately exit
    /// with 259, the same value GetExitCodeProcess reports while the process is still alive (A21-3).
    /// </summary>
    private static bool ProcessObservedGone(ProcessId pid, string instance)
    {
        if (!ProcessTimes.MatchesInstance(pid.Value, instance))
        {
            return true;
        }

        const int access = ProcessQueryLimitedInformation | ProcessSynchronize;
        var handle = Interop.OpenProcess(access, false, pid.Value);
        if (handle == nint.Zero)
        {
            return !ProcessTimes.MatchesInstance(pid.Value, instance);
        }

        var wait = Interop.WaitForSingleObject(handle, 0);
        var readOk = GetExitCodeProcess(handle, out _) != 0;
        Interop.CloseHandle(handle);
        if (wait == WaitObject0 && readOk)
        {
            return true;
        }

        return !ProcessTimes.MatchesInstance(pid.Value, instance);
    }

    private static List<nint> TopLevelWindowsForPid(ProcessId pid)
    {
        var handles = new List<nint>();
        WindowEnum.EnumerateTopLevel(window =>
        {
            if (WindowEnum.LiveWindowOwner(window.Handle) == pid.Value)
            {
                handles.Add(window.Handle);
            }

            return true;
        });
        return handles;
    }

    /// <summary>
    /// Posts WM_CLOSE to one window, reporting whether the request was delivered so the fan-out can tell a
    /// deliberate skip from a send.
    /// </summary>
    private static bool PostWmCloseIfStillOwned(nint hwnd, ProcessId pid, string instance)
    {
        if (WindowEnum.LiveWindowOwner(hwnd) != pid.Value)
        {
            return false;
        }

        if (!ProcessTimes.MatchesInstance(pid.Value, instance))
        {
            return false;
        }

        if (PostMessageW(hwnd, WmClose, nint.Zero, nint.Zero) == 0)
        {
            throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastPInvokeError(), "PostMessageW(WM_CLOSE) failed");
        }

        return true;
    }

    /// <summary>
    /// Verifies the open process handle's creation time still matches the instance token before a terminate
    /// write, so a recycled pid cannot be killed.
    /// </summary>
    private static bool HandleMatchesInstance(nint handle, string instance)
    {
        var read = Interop.GetProcessTimes(handle, out var created, out _, out _, out _) != 0;
        if (!read)
        {
            return false;
        }

        var ticks = ((ulong)created.High << 32) | created.Low;
        if (ticks == 0)
        {
            return false;
        }

        var fromHandle = $"windows-proc-v1:{ticks / TicksPerSecond}:{ticks % TicksPerSecond}";
        return fromHandle == instance;
    }

    private static void RunBefore(Action action)
    {
        try
        {
            action();
        }
        catch (AdapterError error)
        {
            throw BeforeTermination(error);
        }
    }

    private static T ReadBefore<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (AdapterError error)
        {
            throw BeforeTermination(error);
        }
    }

    private static void RunAfter(Action action)
    {
        try
        {
            action();
        }
        catch (AdapterError error)
        {
            throw error.WithDisposition(DeliverySemantics.DeliveredUnverified());
        }
    }

    private static AdapterError BeforeTermination(AdapterError error) =>
        error.WithDisposition(DeliverySemantics.NotDelivered());
}
