using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.System.Window;

/// <summary>
/// Raises and focuses the exact window after stored-identity verification.
/// A handle destroyed and recycled to another process after selection can never be restored,
/// foregrounded, or reported as focused. Check-then-act on an HWND cannot be made atomic, so
/// ownership is enforced on both sides of every write rather than once at entry: the stored pid
/// and process generation are verified here, each native write re-reads the owning pid
/// immediately before issuing it, and success is ownership-qualified: <see cref="IsOwnedForeground"/>
/// requires the foreground window to be this handle and still owned by the expected process.
/// Restore-versus-raise ordering: restore-then-raise when iconic; raise without restore when
/// visible-but-not-foreground. Cross-integrity targets are attempt-and-verify (A21-7); budget
/// exhaustion on a strictly-higher integrity target is activation-worded PERM_DENIED, otherwise
/// ACTION_FAILED with physical_delivery_started: false.
/// </summary>
internal static class WindowActivate
{
    /// <summary>
    /// A21-6: the committed capture recorded 0/5 first-attempt successes, and the scratch-window
    /// foreground transition did not land on the second attempt either. A budget still has to be
    /// finite: an unbounded retry against a target that never becomes foreground would hang the
    /// caller instead of failing closed. Two keeps the one extra attach-and-set-foreground attempt
    /// after a short delay, then exhausts and fails closed.
    /// </summary>
    private const int FocusStealBudget = 2;

    private static readonly global::System.TimeSpan InterAttemptDelay =
        global::System.TimeSpan.FromMilliseconds(25);

    private const int SwShow = 5;
    private const int SwRestore = 9;

    internal static void FocusWindow(WindowInfo win, InteractionLease lease)
    {
        Route(() => lease.Deadline().EnsureRemaining(), BeforeActivation);
        var handle = WindowOps.ParseHandle(win.Id);
        if (handle == 0)
        {
            throw BeforeActivation(new AdapterError(
                ErrorCode.InvalidArgs,
                "window id is not a Windows HWND-shaped id"));
        }

        var evidence = WindowIdentityEvidence.FromInfo(handle, win);
        if (evidence is null)
        {
            throw BeforeActivation(new AdapterError(
                ErrorCode.StaleRef,
                "headed focus requires a process-instance token on the stored window"));
        }

        Route(() => evidence.VerifyStored(), BeforeActivation);
        Route(() => EnsureWindowIsPumping(handle), BeforeActivation);
        Route(() => RestoreIfIconic(handle, evidence), BeforeActivation);
        if (IsOwnedForeground(handle, evidence))
        {
            return;
        }

        var strictlyHigher = ActivationTargetIsStrictlyHigher(win.Pid);
        RaiseWithBudget(handle, evidence, lease, strictlyHigher);
    }

    /// <summary>
    /// Refuses activation of a target that is not dispatching messages. This conservative liveness
    /// check runs after identity verification and before activation, so a destroyed or recycled
    /// handle stays classified as stale.
    /// </summary>
    private static void EnsureWindowIsPumping(nint handle)
    {
        if (WindowEnum.WindowIsResponsive(handle))
        {
            return;
        }

        throw new AdapterError(
                ErrorCode.AppUnresponsive,
                "Target window is not processing messages, so activation would block")
            .WithDetails(new JsonObject { ["physical_delivery_started"] = false })
            .WithSuggestion("Wait for the application to recover before retrying headed input");
    }

    private static bool ActivationTargetIsStrictlyHigher(ProcessId pid)
    {
        return IntegrityIsStrictlyHigher(
            Elevation.CurrentProcessIntegrityRid(),
            Elevation.ProcessIntegrityRid((int)pid.Value));
    }

    /// <summary>
    /// Pure integrity comparison for activation policy: a readable, strictly higher target RID is
    /// the cross-integrity branch (A21-7). Unreadable sides are best-effort, matching the input gate.
    /// </summary>
    internal static bool IntegrityIsStrictlyHigher(uint? callerRid, uint? targetRid)
    {
        return callerRid is uint caller && targetRid is uint target && target > caller;
    }

    private static void RaiseWithBudget(
        nint handle,
        WindowIdentityEvidence evidence,
        InteractionLease lease,
        bool strictlyHigher)
    {
        for (var attempt = 0; attempt < FocusStealBudget; attempt++)
        {
            if (attempt == 0)
            {
                Route(() => lease.Deadline().EnsureRemaining(), BeforeActivation);
            }
            else
            {
                Route(() => lease.Deadline().EnsureRemaining(), AfterForegroundWriteAttempted);
                global::System.Threading.Thread.Sleep(InterAttemptDelay);
                Route(() => lease.Deadline().EnsureRemaining(), AfterForegroundWriteAttempted);
            }

            BringToForeground(handle, evidence);
            if (IsOwnedForeground(handle, evidence))
            {
                return;
            }
        }

        throw BudgetExhausted(strictlyHigher);
    }

    /// <summary>
    /// A lease can expire between budget-loop attempts as well as before the first one, and the two
    /// are different delivery claims. Before the first foreground write the window is untouched, so
    /// that timeout is routed through <see cref="BeforeActivation"/>. From the second attempt onward a
    /// prior write has already reached the live handle, so the envelope reports delivered-unverified.
    /// </summary>
    private static AdapterError AfterForegroundWriteAttempted(AdapterError error)
    {
        return error.WithDisposition(Disposition.DeliveredUnverified());
    }

    internal static AdapterError BudgetExhausted(bool strictlyHigher)
    {
        if (strictlyHigher)
        {
            return Elevation.ActivationElevationDenied();
        }

        return new AdapterError(
                ErrorCode.ActionFailed,
                "Target window did not become foreground before headed delivery")
            .WithDetails(new JsonObject { ["physical_delivery_started"] = false })
            .WithSuggestion("Retry after ensuring the target window can accept focus")
            .WithDisposition(Disposition.NotDelivered());
    }

    /// <summary>
    /// Whether the handle is the foreground window and is still owned by the stored process instance.
    /// Handle equality alone would accept a recycled HWND; pid equality alone would accept a
    /// replacement process that inherited a recycled pid. An unreadable generation read leaves the
    /// window unproven, so this reports false and the caller returns not-delivered.
    /// </summary>
    internal static bool IsOwnedForeground(nint handle, WindowIdentityEvidence evidence)
    {
        return WindowOps.IsForegroundWindow(handle) && OwnsHandleNowOrFalse(evidence);
    }

    private static bool OwnsHandleNowOrFalse(WindowIdentityEvidence evidence)
    {
        try
        {
            return evidence.OwnsHandleNow();
        }
        catch (AdapterError)
        {
            return false;
        }
    }

    private static void RestoreIfIconic(nint handle, WindowIdentityEvidence evidence)
    {
        if (IsIconic(handle) == 0)
        {
            return;
        }

        if (!OwnedForWrite(evidence))
        {
            throw RecycledBeforeForeground();
        }

        if (ShowWindowAsync(handle, SwRestore) == 0)
        {
            throw BeforeActivation(HResult.Win32LastError(
                "ShowWindowAsync failed while restoring target"));
        }
    }

    private static void BringToForeground(nint handle, WindowIdentityEvidence evidence)
    {
        if (!OwnedForWrite(evidence))
        {
            throw RecycledBeforeForeground();
        }

        if (IsWindowVisible(handle) == 0 && ShowWindowAsync(handle, SwShow) == 0)
        {
            throw BeforeActivation(HResult.Win32LastError(
                "ShowWindowAsync failed while showing target"));
        }

        if (!OwnedForWrite(evidence))
        {
            throw RecycledBeforeForeground();
        }

        _ = SetForegroundWindow(handle);
    }

    private static bool OwnedForWrite(WindowIdentityEvidence evidence)
    {
        return evidence.OwnsHandleNow();
    }

    /// <summary>
    /// The handle's owner changed between selection and the foreground write; the write is refused so
    /// a recycled HWND never foregrounds a foreign window. Win32 offers no atomic "act on this window
    /// only while that process still owns it", so the ownership read and the guarded call are two
    /// steps. Success is ownership-qualified, so losing that race yields a foreign window raised once
    /// and an honest not-delivered refusal, never input delivered to it.
    /// </summary>
    private static AdapterError RecycledBeforeForeground()
    {
        return new AdapterError(
                ErrorCode.WindowNotFound,
                "Target window handle changed ownership before foreground delivery")
            .WithDetails(new JsonObject { ["physical_delivery_started"] = false })
            .WithDisposition(Disposition.NotDelivered());
    }

    private static AdapterError BeforeActivation(AdapterError error)
    {
        return error.WithDisposition(Disposition.NotDelivered());
    }

    private static void Route(global::System.Action action, global::System.Func<AdapterError, AdapterError> route)
    {
        try
        {
            action();
        }
        catch (AdapterError error)
        {
            throw route(error);
        }
    }

    [DllImport("user32.dll")]
    private static extern int IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ShowWindowAsync(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern int SetForegroundWindow(nint hWnd);
}
