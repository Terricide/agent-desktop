using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Menu;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Source of the Windows menu wait: the loop that polls the menu-open predicate until the target process's menu state
/// equals the requested state, or the caller's budget is spent.
///
/// A deliberate structural port of the macOS menu wait: the process identity is re-verified before the first predicate
/// read on every iteration, and a second time immediately before success is declared - the macOS double-check that
/// closes the window where the target dies between the read and the return. The deadline is consulted only after a
/// mismatch, so the predicate is evaluated at least once once this loop is reached, and an already-closed menu
/// satisfies a wait-for-closed on that first read.
///
/// That guarantee is this method's, not the command's, and the difference is observable: the caller resolves the target
/// application before calling here, and on a budget smaller than that resolution costs the command refuses with the
/// generic deadline error without ever reaching this loop. A timeout that must survive application resolution has to be
/// larger than resolution costs; this loop cannot rescue a budget that is already spent when it is handed one.
///
/// Core makes exactly one call here and owns no retry of its own, so every condition this loop can absorb - the 50ms
/// poll interval, the identity re-checks, and a predicate read that itself times out mid-poll - is absorbed here
/// rather than propagated raw: a timeout from the predicate falls through to this loop's own deadline check so the
/// direction-specific message still applies, instead of surfacing the predicate's own undecorated timeout.
///
/// Diverges from the macOS mapping of an identity mismatch to <c>AppUnresponsive</c>: this method reports the
/// exited-or-recycled target as <c>StaleRef</c>, the code this crate already uses for a resolved reference that no
/// longer matches live state, rather than the transient-condition code macOS reuses there.
/// </summary>
internal static class SystemWait
{
    private static readonly global::System.TimeSpan PollInterval =
        global::System.TimeSpan.FromMilliseconds(50);

    /// <summary>How long a predicate error waits on the target's process handle for an exit to land before the error is
    /// reported as the target's own. A window that stopped pumping because its process is closing is observed before the
    /// process object is signaled, so a single liveness check cannot tell that teardown from a target that is merely
    /// unresponsive; the handle wait can.</summary>
    private static readonly global::System.TimeSpan ExitConfirmationSlice =
        global::System.TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// The same precedence as the identity mismatch covers a non-timeout predicate error: the menu predicate reports a
    /// target that vanished mid-read as a transient <c>AppUnresponsive</c>, and a target mid-teardown reports it while
    /// its process object is not yet signaled. So before that error leaves the method the loop re-checks liveness and
    /// then waits on the target's process handle, capped by both the exit confirmation slice and the caller's deadline:
    /// a target that exits inside that window surfaces as <c>StaleRef</c>, while a live-but-unresponsive target still
    /// propagates the predicate's own error unchanged once the window passes.
    /// </summary>
    internal static void WaitForMenu(ProcessIdentity process, bool open, Deadline deadline)
    {
        var stateNeverRead = true;
        while (true)
        {
            VerifyProcessAlive(process);
            try
            {
                if (EvaluateMenuState(process.Pid, deadline) == open)
                {
                    VerifyProcessAlive(process);
                    return;
                }

                stateNeverRead = false;
            }
            catch (AdapterError error) when (error.Code != ErrorCode.Timeout)
            {
                throw SettlePredicateError(process, error, deadline);
            }
            catch (AdapterError)
            {
            }

            if (deadline.IsExpired())
            {
                throw deadline.TimeoutError().WithPlatformDetail(ExpiryDetail(open, stateNeverRead));
            }

            global::System.Threading.Thread.Sleep(RemainingSlice(deadline, PollInterval, DirectionMessage(open)));
        }
    }

    private static string DirectionMessage(bool open) =>
        open ? "No menu opened before the deadline" : "Menu did not close before the deadline";

    /// <summary>Separates "the menu never reached the state" from "the menu state was never read at all". Every poll
    /// that times out is absorbed so a slow read does not end the wait early, which is right - but if no poll ever
    /// returned an answer, the deadline message describes a menu this code never actually observed. Saying so points at
    /// the provider rather than at the menu.</summary>
    private static string ExpiryDetail(bool open, bool stateNeverRead)
    {
        var direction = DirectionMessage(open);
        return stateNeverRead
            ? $"{direction}; no poll read the menu state before the deadline"
            : direction;
    }

    /// <summary>Routes every predicate read through one call site so a test can count polls without changing what
    /// production code executes. A predicate read that times out is absorbed by the caller's deadline check, so it
    /// never ends the wait on its own.</summary>
    private static bool EvaluateMenuState(ProcessId pid, Deadline deadline)
    {
#if DEBUG
        Hooks.RecordPollCall();
        if (Hooks.TakePredicateError() is { } forced)
        {
            throw forced;
        }
#endif
        return MenuState.MenuIsOpen(pid, deadline);
    }

    /// <summary>Decides what a non-timeout predicate error means once it has been read: a target that is gone, or that
    /// exits within the bounded confirmation window on its process handle, is <c>StaleRef</c>; a target still alive
    /// after the window keeps the predicate's own error. The window is capped by the caller's deadline, so confirmation
    /// never extends the wait.</summary>
    private static AdapterError SettlePredicateError(
        ProcessIdentity process,
        AdapterError error,
        Deadline deadline)
    {
        try
        {
            VerifyProcessAlive(process);
        }
        catch (AdapterError gone)
        {
            return gone;
        }

        var window = global::System.TimeSpan.FromMilliseconds(deadline.RemainingMs());
        if (ExitedWithin(process, window < ExitConfirmationSlice ? window : ExitConfirmationSlice))
        {
            return StaleProcessError(process);
        }

        return error;
    }

    private static bool ExitedWithin(ProcessIdentity process, global::System.TimeSpan window)
    {
#if DEBUG
        if (Hooks.TakeProcessDeath())
        {
            return true;
        }
#endif
        return ProcessTimes.WaitForExit(process.Pid.Value, window);
    }

    /// <summary>Re-reads the target's generation token and polls its process object. A recycled pid, a pid whose
    /// process is gone, or a process whose object is already signaled all report the target gone.</summary>
    private static void VerifyProcessAlive(ProcessIdentity process)
    {
#if DEBUG
        if (Hooks.TakeProcessDeath())
        {
            throw StaleProcessError(process);
        }
#endif
        if (ProcessTimes.MatchesInstance(process.Pid.Value, process.Instance)
            && ProcessTimes.ProcessIsRunning(process.Pid.Value))
        {
            return;
        }

        throw StaleProcessError(process);
    }

    /// <summary>A target that died mid-wait is a fully understood outcome at the moment this error is built, so it
    /// carries the disposition and recovery an agent needs rather than defaulting to unknown. Nothing was delivered -
    /// the wait only ever observed - and retrying the same wait against the same process instance can never succeed,
    /// because that instance is gone for good; the caller has to resolve the application again. The wording is
    /// process-specific rather than borrowed from the ref-oriented core helper, which tells the caller to re-run a
    /// snapshot for a fresh ref - advice this surface has no refs for.</summary>
    private static AdapterError StaleProcessError(ProcessIdentity process) =>
        new AdapterError(
            ErrorCode.StaleRef,
            "Target process instance is no longer running")
            .WithSuggestion(
                "The application exited during the wait. Re-resolve it with list-apps "
                + "and retry against the new process instance, or treat its exit as the outcome.")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithDetails(new JsonObject
            {
                ["pid"] = process.Pid.Value,
                ["process_instance"] = process.Instance,
            });

    /// <summary>How long this poll may sleep: the poll interval, cut to whatever the caller's budget has left. A budget
    /// already spent throws the shared deadline error carrying this direction, so a sleep is never scheduled past the
    /// expiry.</summary>
    private static global::System.TimeSpan RemainingSlice(
        Deadline deadline,
        global::System.TimeSpan slice,
        string direction)
    {
        var remaining = global::System.TimeSpan.FromMilliseconds(deadline.RemainingMs());
        if (remaining <= global::System.TimeSpan.Zero)
        {
            throw deadline.TimeoutError().WithPlatformDetail(direction);
        }

        return remaining < slice ? remaining : slice;
    }

#if DEBUG
    /// <summary>Test-only seams. Thread-local, so a test arms a fault on its own thread and every later read on that
    /// thread falls through to the real predicate. Compiled only in Debug builds.</summary>
    internal static class Hooks
    {
        [global::System.ThreadStatic]
        private static int _pollCalls;

        [global::System.ThreadStatic]
        private static AdapterError? _forcedPredicateError;

        [global::System.ThreadStatic]
        private static int? _processDeathSkipsRemaining;

        internal static void RecordPollCall() => _pollCalls++;

        internal static int TakePollCalls()
        {
            var count = _pollCalls;
            _pollCalls = 0;
            return count;
        }

        internal static void ArmPredicateError(AdapterError error) => _forcedPredicateError = error;

        internal static AdapterError? TakePredicateError()
        {
            var error = _forcedPredicateError;
            _forcedPredicateError = null;
            return error;
        }

        internal static void ArmProcessDeathAfter(int skips) => _processDeathSkipsRemaining = skips;

        internal static bool TakeProcessDeath()
        {
            switch (_processDeathSkipsRemaining)
            {
                case null:
                    return false;
                case 0:
                    _processDeathSkipsRemaining = null;
                    return true;
                case var remaining:
                    _processDeathSkipsRemaining = remaining - 1;
                    return false;
            }
        }
    }
#endif
}
