using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Windows.Actions;

/// <summary>TogglePattern / InvokePattern transitions for Toggle, Check, Uncheck,
/// ExpandCollapsePattern transitions for Expand and Collapse, and headed SetFocus. Mirrors
/// crates/windows/src/actions/toggle_state.rs, disclosure.rs, and focus.rs.</summary>
internal static class ToggleAndDisclosure
{
    internal const string ToggleLabel = "TogglePattern.Toggle";

    internal const string ExpandLabel = "ExpandCollapsePattern.Expand";

    internal const string CollapseLabel = "ExpandCollapsePattern.Collapse";

    internal const string SetFocusLabel = "UIElement.SetFocus";

    private const int ToggleTimeoutMs = 600;

    private const int ToggleStableMs = 200;

    private const int ToggleSliceMs = 25;

    private const int DisclosureTimeoutMs = 200;

    private const int DisclosureSliceMs = 20;

    internal static readonly ChainDef ToggleChain = new(
        "Refresh the snapshot and retry, or target an element that advertises Toggle or Invoke.",
        false);

    internal static readonly ChainDef DisclosureChain = new(
        "Refresh the snapshot and retry, or target an expandable container.",
        false);

    private const string CheckSuggestion =
        "Refresh the snapshot and inspect the checked state before deciding whether to retry.";

    internal static List<ActionStep> ToggleSteps(
        Element element,
        InteractionPolicy policy,
        Deadline deadline)
    {
        var toggleOk = ToggleAvailable(element);
        var before = toggleOk ? ReadToggleState(element) : null;
        var invokeOk = Chain.InvokeAvailable(element);
        return ToggleJudgedFor(
            deadline,
            policy,
            toggleOk,
            invokeOk,
            () => DeliveredToggleWithObserve(before, deadline, element, PatternToggle),
            () => DeliveredToggleWithObserve(before, deadline, element, Chain.InvokePatternDelivered));
    }

    internal static List<ActionStep> ToggleJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        bool toggleOk,
        bool invokeOk,
        Func<DeliveryOutcome> toggle,
        Func<DeliveryOutcome> invoke) =>
        Chain.Execute(
            deadline,
            ToggleChain,
            policy,
            new[]
            {
                new ChainRung(ToggleLabel, false, Chain.Gated(toggleOk, toggle)),
                new ChainRung(Chain.InvokeLabel, false, Chain.Gated(invokeOk, invoke)),
            });

    internal static List<ActionStep> CheckSteps(Element element, bool wantChecked, Deadline deadline) =>
        CheckUncheckJudgedFor(
            deadline,
            wantChecked,
            ToggleAvailable(element),
            Chain.InvokeAvailable(element),
            () => ToggleAvailable(element) ? ReadToggleState(element) : null,
            () => PatternToggle(element),
            () => Chain.InvokePatternDelivered(element));

    internal static List<ActionStep> CheckUncheckJudgedFor(
        Deadline deadline,
        bool wantChecked,
        bool toggleOk,
        bool invokeOk,
        Func<int?> readState,
        Func<bool> toggle,
        Func<bool> invoke)
    {
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        var before = readState();
        if (MatchesChecked(before, wantChecked))
        {
            return new List<ActionStep> { Chain.BuildStep(Chain.AlreadyLabel, DeliveryOutcome.SatisfiedNoDelivery) };
        }

        var steps = new List<ActionStep>();
        if (toggleOk)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (deadline.IsExpired())
                {
                    throw WindowRoot.DeadlineExceeded(deadline).WithDisposition(Chain.ExhaustionDisposition(steps));
                }

                if (!toggle())
                {
                    break;
                }

                var verified = PollChecked(wantChecked, deadline, readState, attempt == 1 ? null : before);
                steps.Add(Chain.BuildStep(ToggleLabel, Chain.FromDelivery(true, verified)));
                if (verified || !(before.HasValue && readState() is { } after && after != before.Value))
                {
                    return steps;
                }
            }
        }

        if (invokeOk && !Chain.DeliveryOccurred(steps))
        {
            if (deadline.IsExpired())
            {
                throw WindowRoot.DeadlineExceeded(deadline).WithDisposition(Chain.ExhaustionDisposition(steps));
            }

            if (invoke())
            {
                var verified = PollChecked(wantChecked, deadline, readState, null);
                steps.Add(Chain.BuildStep(Chain.InvokeLabel, Chain.FromDelivery(true, verified)));
                return steps;
            }

            steps.Add(Chain.BuildStep(Chain.InvokeLabel, DeliveryOutcome.NotDelivered));
        }

        if (Chain.DeliveryOccurred(steps))
        {
            return steps;
        }

        throw new AdapterError(ErrorCode.ActionFailed, "All chain steps exhausted")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithSuggestion(CheckSuggestion);
    }

    internal static List<ActionStep> DisclosureSteps(
        Element element,
        bool wantExpanded,
        InteractionPolicy policy,
        Deadline deadline)
    {
        var patternOk = ExpandCollapseAvailable(element);
        var current = patternOk ? ReadExpandState(element) : null;
        return DisclosureJudgedFor(
            deadline,
            policy,
            wantExpanded,
            current,
            patternOk,
            Chain.InvokeAvailable(element),
            () => DeliveredDisclosure(element, wantExpanded, deadline, () => PatternExpandCollapse(element, wantExpanded)),
            () => DeliveredDisclosure(element, wantExpanded, deadline, () => Chain.InvokePatternDelivered(element)));
    }

    internal static List<ActionStep> DisclosureJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        bool wantExpanded,
        int? current,
        bool patternOk,
        bool invokeOk,
        Func<DeliveryOutcome> pattern,
        Func<DeliveryOutcome> invoke)
    {
        var targetState = wantExpanded ? 1 : 0;
        var oppositeState = wantExpanded ? 0 : 1;
        if (current == targetState)
        {
            return new List<ActionStep> { Chain.BuildStep(Chain.AlreadyLabel, DeliveryOutcome.SatisfiedNoDelivery) };
        }

        var allowPattern = current != targetState && current != 3;
        var allowInvoke = invokeOk && current == oppositeState;
        var label = wantExpanded ? ExpandLabel : CollapseLabel;
        return Chain.Execute(
            deadline,
            DisclosureChain,
            policy,
            new[]
            {
                new ChainRung(label, false, Chain.Gated(patternOk && allowPattern, pattern)),
                new ChainRung(Chain.InvokeLabel, false, Chain.Gated(allowInvoke, invoke)),
            });
    }

    internal static ActionResult FocusElement(Element element, bool headed, Deadline deadline)
    {
        if (!headed)
        {
            throw new AdapterError(ErrorCode.PolicyDenied, "SetFocus moves the desktop foreground window on Windows")
                .WithSuggestion("Pass --headed when keyboard focus should move the desktop foreground window")
                .WithDetails(new JsonObject
                {
                    ["foreground_effect"] = true,
                    ["evidence"] = new JsonArray("A3-4", "A19-5"),
                })
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        var hr = element.SetFocus();
        var delivered = hr >= 0 || Chain.ClassifyWriteOrNotDelivered(hr, "SetFocus", SetFocusLabel);
        if (!delivered)
        {
            throw new AdapterError(ErrorCode.ActionFailed, "SetFocus is not available on this element")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        var verified = element.CurrentHasKeyboardFocus().Value == true || FocusMatchesTarget(element);
        return ActionResult.FromExecution(
            new Action { Kind = ActionKind.SetFocus },
            new List<ActionStep> { new(SetFocusLabel, "succeeded", Chain.SemanticMechanism, verified) });
    }

    private static bool ToggleAvailable(Element element) =>
        element.CurrentProperty(PropertyIds.IsTogglePatternAvailable).Value is true;

    private static int? ReadToggleState(Element element) =>
        element.CurrentProperty(PropertyIds.ToggleState).Value is int state && state is >= 0 and <= 2 ? state : null;

    private static bool MatchesChecked(int? state, bool wantChecked) =>
        (state == 1 && wantChecked) || (state == 0 && !wantChecked);

    private static bool PatternToggle(Element element) =>
        Chain.InvokeVoidPattern(element, Slots.PatternToggle, Slots.TogglePatternToggle, "Toggle", ToggleLabel);

    private static DeliveryOutcome DeliveredToggleWithObserve(
        int? before,
        Deadline deadline,
        Element element,
        Func<Element, bool> deliver)
    {
        if (!deliver(element))
        {
            return DeliveryOutcome.NotDelivered;
        }

        if (!before.HasValue)
        {
            return DeliveryOutcome.DeliveredUnverified;
        }

        int? candidate = null;
        long candidateSince = 0;
        var verified = Chain.PollVerify(deadline, ToggleTimeoutMs, ToggleSliceMs, () =>
        {
            var current = ReadToggleState(element);
            if (!current.HasValue || current.Value == before.Value)
            {
                candidate = null;
                return false;
            }

            var now = Deadline.Now();
            if (candidate != current.Value)
            {
                candidate = current.Value;
                candidateSince = now;
                return false;
            }

            return now - candidateSince >= ToggleStableMs;
        });

        return Chain.FromDelivery(true, verified);
    }

    private static bool PollChecked(bool wantChecked, Deadline deadline, Func<int?> readState, int? earlyExitFrom) =>
        Chain.PollVerify(deadline, ToggleTimeoutMs, ToggleSliceMs, () =>
        {
            var state = readState();
            if (MatchesChecked(state, wantChecked))
            {
                return true;
            }

            if (earlyExitFrom.HasValue && state.HasValue && state.Value != earlyExitFrom.Value)
            {
                Thread.Sleep(ToggleSliceMs);
                return MatchesChecked(readState(), wantChecked);
            }

            return false;
        });

    private static bool ExpandCollapseAvailable(Element element) =>
        element.CurrentProperty(PropertyIds.IsExpandCollapsePatternAvailable).Value is true;

    private static int? ReadExpandState(Element element) =>
        element.CurrentProperty(PropertyIds.ExpandCollapseState).Value is int state && state is >= 0 and <= 3 ? state : null;

    private static bool PatternExpandCollapse(Element element, bool wantExpanded) =>
        Chain.InvokeVoidPattern(
            element,
            Slots.PatternExpandCollapse,
            wantExpanded ? Slots.ExpandCollapsePatternExpand : Slots.ExpandCollapsePatternCollapse,
            wantExpanded ? "Expand" : "Collapse",
            wantExpanded ? ExpandLabel : CollapseLabel);

    private static DeliveryOutcome DeliveredDisclosure(
        Element element,
        bool wantExpanded,
        Deadline deadline,
        Func<bool> deliver)
    {
        if (!deliver())
        {
            return DeliveryOutcome.NotDelivered;
        }

        var targetState = wantExpanded ? 1 : 0;
        var patternOk = ExpandCollapseAvailable(element);
        var verified = Chain.PollVerify(
            deadline,
            DisclosureTimeoutMs,
            DisclosureSliceMs,
            () => patternOk && ReadExpandState(element) == targetState);
        return Chain.FromDelivery(true, verified);
    }

    private static bool FocusMatchesTarget(Element element)
    {
        if (Client.Create(out var client) < 0 || client is null)
        {
            return false;
        }

        using (client)
        {
            if (client.FocusedElement(out var focused) < 0 || focused == IntPtr.Zero)
            {
                Calls.Release(focused);
                return false;
            }

            try
            {
                return client.CompareElements(element.Pointer, focused, out var same) >= 0 && same;
            }
            finally
            {
                Calls.Release(focused);
            }
        }
    }
}
