using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.System.Window;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>Shared preflight for ref-addressed physical input: process identity, the UIPI
/// integrity gate, the window foreground check, and the bounds-derived delivery point. Mirrors
/// crates/windows/src/actions/physical_target.rs.
///
/// Every gate refuses before any input is posted, so a refusal always means nothing was
/// delivered. The Rust source carries a <c>cfg(not(target_os = "windows"))</c> variant of the host
/// window climb that answers no window; this project is Windows-only, so that variant is not
/// ported, and no <c>host_window_handle_for_test</c> accessor is ported because there is no test
/// lane for it in this project yet.</summary>
internal static class PhysicalTarget
{
    /// <summary>The target's owning process: the verified process recorded at capture when one
    /// exists, otherwise the live process id read from the element. A target with no readable
    /// owner is refused as a stale ref, because its integrity boundary cannot be established.</summary>
    internal static ProcessId TargetProcessId(Element element)
    {
        if (element.VerifiedProcess is { } verified)
        {
            return ProcessId.From(verified.Pid);
        }

        var live = element.CurrentProcessId();
        if (live is null)
        {
            throw new AdapterError(
                ErrorCode.StaleRef,
                "Physical input target no longer has a valid owning process");
        }

        return ProcessId.From((uint)live.Value);
    }

    /// <summary>Refuses when the target's owning process sits above the caller's integrity
    /// level: UIPI blocks synthetic input across that boundary, so the refusal names a
    /// permission denial before any key or mouse event is posted.</summary>
    internal static void EnsureElevationAllows(Element element) =>
        Elevation.EnsureTargetIntegrityAllowsInput((int)TargetProcessId(element).Value);

    /// <summary>Refuses a physical keyboard leg when the interaction policy does not authorize
    /// the focus-steal the leg performs.
    ///
    /// The C# <c>InteractionPolicy</c> is one value, not the Rust policy's two flags.
    /// <c>Physical</c> is the Rust <c>headed()</c> policy, the only value that authorizes
    /// focus-steal, so a policy that is not <c>Physical</c> is a policy with
    /// <c>allow_focus_steal</c> false.</summary>
    internal static void EnsureKeyboardPolicy(InteractionPolicy policy)
    {
        if (policy == InteractionPolicy.Physical)
        {
            return;
        }

        throw PolicyDeniedForPolicy("Physical keyboard fallback requires focus permission", policy);
    }

    /// <summary>Refuses a physical click leg when the policy does not authorize the cursor-move
    /// and focus-steal a real click performs. Both flags are required, so a policy that
    /// authorizes only one of them is refused here too, and the only C# value that authorizes
    /// both is <c>Physical</c>.</summary>
    internal static void EnsureHeadedClickPolicy(InteractionPolicy policy)
    {
        if (policy == InteractionPolicy.Physical)
        {
            return;
        }

        throw PolicyDeniedForPolicy(
            "Physical click fallback is disabled by the current interaction policy",
            policy);
    }

    /// <summary>Establishes that a physical click can be delivered: the remaining budget, the
    /// verified process corroboration, the integrity gate, then the foreground answer for the
    /// window hosting the target. The caller refuses delivery when the answer is false.</summary>
    internal static bool EnsureClickDeliveryReady(Element element, Deadline deadline)
    {
        deadline.EnsureRemaining();
        LiveRead.CorroborateVerifiedProcess(element);
        EnsureElevationAllows(element);
        return TargetWindowIsForeground(element, deadline);
    }

    /// <summary>Establishes that a physical keystroke can be delivered: the remaining budget, the
    /// verified process corroboration, the integrity gate, then whether the target itself holds
    /// keyboard focus. No foreground check, matching the Rust source.</summary>
    internal static bool EnsureKeyboardDeliveryReady(Element element, Deadline deadline)
    {
        deadline.EnsureRemaining();
        LiveRead.CorroborateVerifiedProcess(element);
        EnsureElevationAllows(element);
        return Focus.KeyboardFocusReady(element);
    }

    /// <summary>The point to post physical input at: the actionability-verified point when one
    /// was established, otherwise the midpoint of the target's live bounds. A point outside
    /// those bounds is refused, because posting at it would land on a surface the target does
    /// not own, and the refusal carries the fresh-snapshot recovery a stale ref needs.</summary>
    internal static Point DeliveryPoint(Rect bounds, Point? verified)
    {
        var point = verified ?? new Point(
            bounds.X + bounds.Width / 2.0,
            bounds.Y + bounds.Height / 2.0);

        if (point.X < bounds.X
            || point.X > bounds.X + bounds.Width
            || point.Y < bounds.Y
            || point.Y > bounds.Y + bounds.Height)
        {
            throw new AdapterError(
                ErrorCode.StaleRef,
                "Actionability-verified input point is outside the target's live bounds")
                .WithSuggestion("Run 'snapshot' to refresh, then retry with the updated ref.")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return point;
    }

    /// <summary>The target's live bounds for the delivery midpoint. A zero-size extent is
    /// refused as a hidden or off-screen element, a bounds read the provider reported as empty
    /// is refused as bounds the click cannot use, and a bounds read that never happened is
    /// refused as evidence rather than read as empty bounds.
    ///
    /// SEAM: <c>read_one</c> in crates/windows/src/tree/properties/mod.rs is not ported. The
    /// live bounds slot it reads for <c>BoundingRectangle</c> is the same UI Automation call as
    /// <c>Element::CurrentBoundingRectangle</c>, so the bounds are read through it.</summary>
    internal static Rect ReadClickBounds(Element element)
    {
        var field = element.CurrentBoundingRectangle();
        if (field.IsKnown())
        {
            var bounds = field.Value;
            if (bounds.Width <= 0.0 || bounds.Height <= 0.0)
            {
                throw new AdapterError(ErrorCode.ActionFailed, "Element has zero-size bounds")
                    .WithSuggestion("Element may be hidden or off-screen. Try 'scroll-to' first.");
            }

            return bounds;
        }

        if (field.IsAbsent())
        {
            throw new AdapterError(ErrorCode.ActionFailed, "Element has no readable bounds")
                .WithSuggestion("Physical click requires readable bounds");
        }

        throw new AdapterError(ErrorCode.ActionFailed, "Could not read target bounds for physical click");
    }

    /// <summary>The refusal raised when the target lost focus before physical input was
    /// posted. Its disposition is NOT_DELIVERED, because the leg refuses before posting.</summary>
    internal static AdapterError FocusLostBeforeDelivery() =>
        new AdapterError(
            ErrorCode.ActionFailed,
            "Target element lost focus before physical input delivery")
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>Whether the window hosting the target owns the desktop foreground. A handle that
    /// the climb did not resolve answers false, so no input is posted.</summary>
    private static bool TargetWindowIsForeground(Element element, Deadline deadline) =>
        HostWindowHandle(element, deadline) is nint hwnd && WindowOps.IsRootForegroundWindow(hwnd);

    /// <summary>Walks up from the target to the first ancestor reporting a non-zero window
    /// handle. UIA reports handle 0 - success, not failure - for every element that is not
    /// itself a window, which is the normal shape for WPF, WinUI, UWP and Chromium content, so
    /// reading the leaf's handle alone would answer "no window, not foreground" for most modern
    /// UI and refuse delivery before injecting.
    ///
    /// SEAM: <c>first_native_hwnd</c> in crates/windows/src/tree/hit_test/corroborate.rs cannot
    /// be called: its walker parameter is a type the project does not declare, so the climb
    /// cannot be run at all. The seam answers no window, which is the Rust source's own "climb
    /// completed and no ancestor owns a window" branch, so the foreground check answers false
    /// and no input is posted. The client and walker plumbing is kept so the climb plugs in
    /// where the Rust source reads it.</summary>
    private static nint? HostWindowHandle(Element element, Deadline deadline)
    {
        var createHr = Client.Create(out var client);
        if (createHr < 0 || client is null)
        {
            return null;
        }

        using (client)
        {
            var walkerHr = client.RawViewWalker(out nint walker);
            if (walkerHr < 0 || walker == 0)
            {
                return null;
            }

            try
            {
                return FirstNativeHwnd(element, walker, deadline);
            }
            finally
            {
                Calls.Release(walker);
            }
        }
    }

    /// <summary>SEAM for <c>first_native_hwnd</c>: the climb to the first ancestor reporting a
    /// non-zero handle. Answers no window, so the foreground check refuses delivery.</summary>
    private static nint? FirstNativeHwnd(Element element, nint walker, Deadline deadline) => null;

    /// <summary>The refusal <c>AdapterError::policy_denied_for_policy</c> raises: POLICY_DENIED,
    /// the suggestion the policy's flags call for, and a NOT_DELIVERED disposition. The shared
    /// core helper is not ported, so the refusal is built here from the same parts.</summary>
    private static AdapterError PolicyDeniedForPolicy(string message, InteractionPolicy policy) =>
        new AdapterError(ErrorCode.PolicyDenied, message)
            .WithSuggestion(PolicyDeniedSuggestion(policy))
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>The suggestion for a refused physical leg, chosen by what the policy authorizes.
    /// A policy that authorizes focus-steal but not cursor-move gets the cursor suggestion; a
    /// policy that authorizes neither is the headless policy and gets the headless recovery.</summary>
    private static string PolicyDeniedSuggestion(InteractionPolicy policy) =>
        policy == InteractionPolicy.Unspecified
            ? "Headless mode allows only accessibility-backed actions; "
              + "enable physical interaction in the policy (CLI: --headed) only if cursor/focus "
              + "movement is intended, otherwise refresh the snapshot or target an element "
              + "with the needed semantic action"
            : "Use an explicit mouse/focus command if physical interaction is intended";}
