using System.Collections.Generic;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Liveness and hang classification for a process identity. The wait gate decides whether a process
/// has exited; the window-hang probe decides between Running and Unresponsive for a live process.
/// </summary>
internal static partial class ProcessStateProbe
{
    /// <summary>
    /// Result of one hang-probe pass over a process's top-level windows. Kept free of Win32 so the
    /// two-signal threshold (one transient hung pass must not classify Unresponsive; two consecutive
    /// must) is testable without a live hung window (A21-4).
    /// </summary>
    internal enum HangProbeResult
    {
        Responsive,
        Hung,
    }

    private const uint CrashNibbleMask = 0xF000_0000;
    private const uint CrashNibbleValue = 0xC000_0000;
    private const int MaxProbedWindows = 16;
    private const ulong PerProbeBudgetFraction = 8;
    private const ulong PerProbeCapMs = 500;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 0x0000_0102;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSynchronize = 0x0010_0000;

    /// <summary>
    /// Pure hang classification for a process already known to be alive. Free of platform APIs so the
    /// retry threshold is testable on every host, matching the macOS classify shape. A probe that
    /// fails throws, and the exception propagates to the caller.
    /// </summary>
    internal static ProcessState ClassifyHang(global::System.Func<HangProbeResult> probe)
    {
        if (probe() == HangProbeResult.Responsive)
        {
            return ProcessState.Running;
        }

        return probe() == HangProbeResult.Responsive ? ProcessState.Running : ProcessState.Unresponsive;
    }

    /// <summary>
    /// Maps a wait-confirmed exit code onto Exited or Crashed (A21-3). Crashed when the high nibble is
    /// 0xC (NTSTATUS unhandled-exception shape); otherwise Exited. The wait gate, not this helper,
    /// decides whether the process is gone: a live process whose code reads as STILL_ACTIVE (259)
    /// must never reach here.
    /// </summary>
    internal static ProcessState StateFromExitCode(uint code)
    {
        if ((code & CrashNibbleMask) == CrashNibbleValue)
        {
            return ProcessState.CrashedWith(unchecked((int)code));
        }

        return ProcessState.ExitedWith(unchecked((int)code));
    }

    /// <summary>
    /// Reads the state of a process identity. A process whose instance no longer matches the identity
    /// (before or after classification) is reported as Exited with no code, because the pid now names
    /// a different process.
    /// </summary>
    internal static ProcessState ReadProcessState(ProcessIdentity process, Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (!ProcessIdentityCheck.MatchesInstance(process.Pid, process.Instance))
        {
            return ProcessState.ExitedWith(null);
        }

        nint handle;
        try
        {
            handle = OpenProcessForState(process.Pid);
        }
        catch (AdapterError)
        {
            if (!ProcessIdentityCheck.MatchesInstance(process.Pid, process.Instance))
            {
                return ProcessState.ExitedWith(null);
            }

            throw;
        }

        ProcessState state;
        try
        {
            state = ClassifyOpened(handle, process.Pid, deadline);
        }
        finally
        {
            CloseHandle(handle);
        }

        return ProcessIdentityCheck.MatchesInstance(process.Pid, process.Instance)
            ? state
            : ProcessState.ExitedWith(null);
    }

    /// <summary>
    /// WaitForSingleObject on a process handle settles into one of three outcomes, and only one means
    /// the process is alive. WAIT_OBJECT_0 (signaled: the process exited, so its exit code is read and
    /// classified). WAIT_TIMEOUT (unsignaled: the process is running, so classification proceeds to the
    /// window-hang probe). WAIT_FAILED (the wait could not judge liveness, most often because the handle
    /// lacks SYNCHRONIZE rights, e.g. across an integrity boundary). A failed wait is not a timeout under
    /// another name: folding it into the WAIT_TIMEOUT path would report a process this call cannot see
    /// as confidently alive, so it surfaces as the classified Win32 error instead of an invented state.
    /// </summary>
    private static ProcessState ClassifyOpened(nint handle, ProcessId pid, Deadline deadline)
    {
        deadline.EnsureRemaining();
        uint wait = WaitForSingleObject(handle, 0);
        if (wait == WaitObject0)
        {
            if (GetExitCodeProcess(handle, out uint code) == 0)
            {
                throw Win32Errors.FromLastError("GetExitCodeProcess failed after the process handle signaled");
            }

            return StateFromExitCode(code);
        }

        if (wait != WaitTimeout)
        {
            throw Win32Errors.FromLastError("WaitForSingleObject failed and could not judge process liveness");
        }

        IReadOnlyList<nint> windows = TopLevelWindowsFor(pid);
        if (windows.Count == 0)
        {
            return ProcessState.Running;
        }

        return ClassifyHang(() =>
        {
            deadline.EnsureRemaining();
            return ProbeWindowsOnce(windows, deadline);
        });
    }

    private static HangProbeResult ProbeWindowsOnce(IReadOnlyList<nint> windows, Deadline deadline)
    {
        for (int index = 0; index < windows.Count; index++)
        {
            if (index > 0)
            {
                deadline.EnsureRemaining();
            }

            if (WindowIsHung(windows[index], deadline))
            {
                return HangProbeResult.Hung;
            }
        }

        return HangProbeResult.Responsive;
    }

    private static bool WindowIsHung(nint hwnd, Deadline deadline)
    {
        if (IsHungAppWindow(hwnd) == 0)
        {
            return false;
        }

        ulong probeMs = ProbeTimeoutMs(deadline);
        return !Automation.WindowIsPumping(hwnd, probeMs);
    }

    private static ulong ProbeTimeoutMs(Deadline deadline)
    {
        ulong budgetMs = (ulong)deadline.RemainingMs() / PerProbeBudgetFraction;
        return global::System.Math.Clamp(budgetMs, 1UL, PerProbeCapMs);
    }

    private static IReadOnlyList<nint> TopLevelWindowsFor(ProcessId pid)
    {
        List<nint> handles = new();
        WindowEnum.EnumerateTopLevel(window =>
        {
            if (handles.Count >= MaxProbedWindows)
            {
                return false;
            }

            if (WindowIdentity.LiveWindowOwner(window.Handle) == pid)
            {
                handles.Add(window.Handle);
            }

            return true;
        });
        return handles;
    }

    private static nint OpenProcessForState(ProcessId pid)
    {
        uint access = ProcessQueryLimitedInformation | ProcessSynchronize;
        nint handle = OpenProcess(access, 0, pid.Value);
        if (handle == nint.Zero)
        {
            throw Win32Errors.FromLastError("OpenProcess failed for process_state liveness check");
        }

        return handle;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint desiredAccess, int inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetExitCodeProcess(nint handle, out uint exitCode);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("user32.dll")]
    private static partial int IsHungAppWindow(nint hwnd);
}
