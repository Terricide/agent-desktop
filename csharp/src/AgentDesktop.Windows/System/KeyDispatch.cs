using System.Diagnostics;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.System.Window;

namespace AgentDesktop.Windows.System;

/// <summary>Delivers a key combo to a named process by composing the keyboard primitive under a
/// verify-only focus gate. Core's headed path already activates the target before this runs; this
/// type never calls window activation.</summary>
internal static class KeyDispatch
{
    private static readonly TimeSpan KeyboardFocusWait = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan KeyboardFocusPoll = TimeSpan.FromMilliseconds(5);
    private const int GuiThreadInfoCbSizeOffset = 0;
    private const int HwndActiveOffset = 8;
    private const int HandleCount = 6;
    private const int CaretRectBytes = 16;

    /// <summary>Delivers the chord only after every gate passes: process identity, integrity,
    /// foreground ownership, keyboard focus, and a final foreground read. A target that is recycled,
    /// elevated, backgrounded, or unfocused stops the delivery before any physical key event exists.</summary>
    internal static ActionResult PressForApp(
        ProcessIdentity process,
        KeyCombo combo,
        InteractionPolicy policy,
        Deadline deadline)
    {
        deadline.EnsureRemaining();
        RequireProcess(process);
        EnsureInjectionIntegrity(process.Pid);
        EnsureForegroundForDelivery(process, policy);
        RequireProcess(process);
        RequireKeyboardFocus(process.Pid, process.Instance, deadline);
        RequireProcess(process);
        KeyDispatchHooks.BetweenVerifyAndInject.Run();
        if (!ProcessOwnsForeground(process.Pid, process.Instance))
        {
            throw FocusLostBeforeDelivery(process.Pid);
        }

        RequireProcess(process);
        RunSynthesize(combo, deadline);
        return PostDeliveryProcessResult(process);
    }

    /// <summary>Runs the shared elevation gate ahead of any delivery. A refusal is reframed so its
    /// details describe the whole delivery as never started.</summary>
    /// <remarks>The test hook feeds the gate synthetic integrity RIDs, so a higher-integrity refusal
    /// can be driven with no elevated process present.</remarks>
    private static void EnsureInjectionIntegrity(ProcessId pid)
    {
        try
        {
            if (KeyDispatchHooks.ForceHigherIntegrity.IsActive())
            {
                Elevation.EvaluateIntegrityGate(0x2000u, 0x3000u);
                return;
            }

            Elevation.EnsureTargetIntegrityAllowsInput((int)pid.Value);
        }
        catch (AdapterError error)
        {
            throw AsPredeliveryRefusal(error);
        }
    }

    /// <summary>Reframes the shared elevation gate's <c>raw_input_emitted</c> claim into this
    /// type's <c>physical_delivery_started</c> vocabulary. The gate's own wording stays unchanged
    /// for its other callers, which sit immediately ahead of a raw injection call.</summary>
    private static AdapterError AsPredeliveryRefusal(AdapterError error)
    {
        if (error.Details is null || JsonNode.Parse(error.Details) is not JsonObject details)
        {
            return error;
        }

        var claim = details["raw_input_emitted"];
        if (claim is null)
        {
            return error;
        }

        var cloned = claim.DeepClone();
        details.Remove("raw_input_emitted");
        details["physical_delivery_started"] = cloned;
        return error.WithDetails(details.ToJsonString());
    }

    private static void EnsureForegroundForDelivery(ProcessIdentity process, InteractionPolicy policy)
    {
        if (ProcessOwnsForeground(process.Pid, process.Instance))
        {
            return;
        }

        throw AllowsFocusSteal(policy)
            ? FocusVerifyFailed(process.Pid)
            : BackgroundFailClosed(process.Pid);
    }

    /// <summary>Physical is the only policy the Windows keyboard legs authorize to take focus, so it
    /// is the policy that maps to a focus-steal allowance.</summary>
    private static bool AllowsFocusSteal(InteractionPolicy policy) => policy == InteractionPolicy.Physical;

    /// <summary>Confirms the named process generation still exists. A recycled pid fails with
    /// <c>APP_UNRESPONSIVE</c>, so no key event is attributed to a stranger.</summary>
    private static void RequireProcess(ProcessIdentity process)
    {
        if (ProcessTimes.MatchesInstance(process.Pid.Value, process.Instance))
        {
            return;
        }

        throw new AdapterError(ErrorCode.AppUnresponsive, "Target process instance is no longer running")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithDetails(new JsonObject
            {
                ["pid"] = process.Pid.Value,
                ["process_instance"] = process.Instance,
                ["complete"] = false,
                ["physical_delivery_started"] = false,
            });
    }

    /// <summary>Reports a completed dispatch as unverified. A target that is gone after the chord
    /// cannot be given a disposition that claims it landed, so its refusal is marked delivered-unverified.</summary>
    private static ActionResult PostDeliveryProcessResult(ProcessIdentity process)
    {
        try
        {
            RequireProcess(process);
        }
        catch (AdapterError error)
        {
            throw error.WithDisposition(DeliverySemantics.DeliveredUnverified());
        }

        return ActionResult.DeliveredUnverified("press_key");
    }

    /// <summary>Waits for keyboard focus on the target, bounded by <c>KeyboardFocusWait</c> and the
    /// caller's remaining budget. Focus and the process generation are both required before the
    /// delivery may start.</summary>
    private static void RequireKeyboardFocus(ProcessId pid, string instance, Deadline deadline)
    {
        var localDeadline = BoundedTimestamp(deadline, KeyboardFocusWait);
        while (true)
        {
            deadline.EnsureRemaining();
            if (ProcessHoldsKeyboardFocus(pid) && ProcessTimes.MatchesInstance(pid.Value, instance))
            {
                return;
            }

            if (Stopwatch.GetTimestamp() >= localDeadline)
            {
                throw new AdapterError(
                    ErrorCode.ActionFailed,
                    "Application has no verified keyboard focus for key delivery")
                    .WithDetails(new JsonObject
                    {
                        ["pid"] = pid.Value,
                        ["physical_delivery_started"] = false,
                    })
                    .WithDisposition(DeliverySemantics.NotDelivered());
            }

            Thread.Sleep(RemainingSlice(deadline, KeyboardFocusPoll));
        }
    }

    /// <summary>The shorter of <paramref name="maximum"/> and the remaining budget, as a sleep
    /// duration. An expired budget throws the shared timeout error.</summary>
    private static TimeSpan RemainingSlice(Deadline deadline, TimeSpan maximum)
    {
        deadline.EnsureRemaining();
        var remainingTicks = deadline.ExpiresAt - Stopwatch.GetTimestamp();
        var remaining = TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
        return remaining < maximum ? remaining : maximum;
    }

    /// <summary>The stopwatch timestamp the local focus wait expires at. A stamp that overflows is
    /// refused as an argument error rather than run against a wrapped deadline.</summary>
    private static long BoundedTimestamp(Deadline deadline, TimeSpan maximum)
    {
        var slice = RemainingSlice(deadline, maximum);
        try
        {
            return checked(Stopwatch.GetTimestamp() + (long)(slice.TotalSeconds * Stopwatch.Frequency));
        }
        catch (OverflowException)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "Deadline is out of range");
        }
    }

    private static void RunSynthesize(KeyCombo combo, Deadline deadline)
    {
        KeyDispatchHooks.SynthesisProbe.Note();
        KeyboardEvent.SynthesizeKey(combo, deadline);
    }

    private static AdapterError FocusVerifyFailed(ProcessId pid) =>
        FocusLostBeforeDelivery(pid)
            .WithSuggestion("Retry after ensuring the target application remains frontmost");

    private static AdapterError BackgroundFailClosed(ProcessId pid) =>
        new AdapterError(
            ErrorCode.ActionFailed,
            "Target application is not foreground; Windows SendInput cannot deliver to a background process")
            .WithDetails(new JsonObject
            {
                ["pid"] = pid.Value,
                ["physical_delivery_started"] = false,
            })
            .WithSuggestion(
                "Retry with --headed so the target can be activated first, or ensure the target is already foreground")
            .WithDisposition(DeliverySemantics.NotDelivered());

    private static AdapterError FocusLostBeforeDelivery(ProcessId pid) =>
        new AdapterError(
            ErrorCode.ActionFailed,
            "Target application lost focus before physical input delivery")
            .WithDetails(new JsonObject
            {
                ["pid"] = pid.Value,
                ["physical_delivery_started"] = false,
            })
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>Whether the target owns the foreground window right now and still names the expected
    /// process generation. Any unreadable foreground yields <c>false</c>, which fails closed.</summary>
    private static bool ProcessOwnsForeground(ProcessId pid, string instance)
    {
        if (KeyDispatchHooks.ForceFocusLost.IsActive())
        {
            return false;
        }

        var foreground = Interop.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        if (WindowIdentity.LiveWindowOwner(foreground) != pid)
        {
            return false;
        }

        return ProcessTimes.MatchesInstance(pid.Value, instance);
    }

    /// <summary>Whether the target process holds the GUI thread's keyboard focus, read from the focused
    /// window or, when none is focused, the active window.</summary>
    private static bool ProcessHoldsKeyboardFocus(ProcessId pid)
    {
        if (KeyDispatchHooks.ForceKeyboardFocusDenied.IsActive())
        {
            return false;
        }

        var info = new byte[GuiThreadInfoSize()];
        BitConverter.GetBytes((uint)info.Length).CopyTo(info, GuiThreadInfoCbSizeOffset);
        if (GetGUIThreadInfo(0, info) == 0)
        {
            return false;
        }

        var focus = ReadHandle(info, HwndFocusOffset());
        if (focus == IntPtr.Zero)
        {
            focus = ReadHandle(info, HwndActiveOffset);
        }

        if (focus == IntPtr.Zero)
        {
            return false;
        }

        return WindowIdentity.LiveWindowOwner(focus) == pid;
    }

    /// <summary>The size of the native GUITHREADINFO record: its size and flags words, six handles, and a caret rectangle.</summary>
    private static int GuiThreadInfoSize() => 8 + HandleCount * IntPtr.Size + CaretRectBytes;

    /// <summary>The offset of <c>hwndFocus</c>, which follows <c>hwndActive</c> by one handle.</summary>
    private static int HwndFocusOffset() => HwndActiveOffset + IntPtr.Size;

    private static IntPtr ReadHandle(byte[] buffer, int offset) =>
        IntPtr.Size == 8
            ? new IntPtr(BitConverter.ToInt64(buffer, offset))
            : new IntPtr(BitConverter.ToInt32(buffer, offset));

    [global::System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetGUIThreadInfo(uint threadId, byte[] info);
}
