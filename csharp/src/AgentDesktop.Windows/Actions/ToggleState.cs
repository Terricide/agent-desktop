using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// Toggle / Check / Uncheck via TogglePattern with Invoke fallback (A19-2).
internal static class ToggleState
{
    internal const string ToggleLabel = "TogglePattern.Toggle";

    private const string CheckSuggestion =
        "Refresh the snapshot and inspect the checked state before deciding whether to retry.";

    private static readonly TimeSpan ToggleTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan ToggleStable = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PollSlice = TimeSpan.FromMilliseconds(25);

    /// Chain definition for the Toggle rung pair; its suggestion is surfaced when the chain yields no delivery.
    internal static readonly ChainDef ToggleChainDef = new(
        "Refresh the snapshot and retry, or target an element that advertises Toggle or Invoke.",
        ContinueAfterUnverifiedDelivery: false);

    /// UIA `ToggleState` values (A15-7 / A19-2).
    internal enum ToggleKind
    {
        Off,
        On,
        Indeterminate,
    }

    /// Rung availability for the Toggle chain, bundled so
    /// <see cref="ToggleJudgedFor"/> stays within the 5-parameter cap.
    internal sealed record ToggleAvailability(bool ToggleOk, bool InvokeOk);

    /// Injected Check/Uncheck plan: target state plus rung availability,
    /// bundled so <see cref="CheckUncheckJudgedFor"/> stays within the 5-parameter cap.
    internal sealed record CheckUncheckPlan(bool WantChecked, bool ToggleOk, bool InvokeOk);

    public static List<ActionStep> ToggleSteps(Element element, InteractionPolicy policy, Deadline deadline)
    {
        var toggleOk = ToggleAvailable(element);
        var before = toggleOk ? ReadToggleState(element) : null;
        var availability = new ToggleAvailability(toggleOk, Chain.InvokeAvailable(element));
        return ToggleJudgedFor(
            deadline,
            policy,
            availability,
            () => DeliveredWithObserve(before, deadline, element, PatternToggle),
            () => DeliveredWithObserve(before, deadline, element, Chain.InvokePatternDelivered));
    }

    public static List<ActionStep> CheckSteps(Element element, InteractionPolicy policy, Deadline deadline) =>
        CheckUncheckSteps(element, true, deadline);

    public static List<ActionStep> UncheckSteps(Element element, InteractionPolicy policy, Deadline deadline) =>
        CheckUncheckSteps(element, false, deadline);

    private static List<ActionStep> CheckUncheckSteps(Element element, bool wantChecked, Deadline deadline) =>
        CheckUncheckJudgedFor(
            deadline,
            new CheckUncheckPlan(wantChecked, ToggleAvailable(element), Chain.InvokeAvailable(element)),
            () => ReadToggleKind(element),
            () => PatternToggle(element),
            () => Chain.InvokePatternDelivered(element));

    /// Injected Toggle chain: unit-test seam and live path.
    internal static List<ActionStep> ToggleJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        ToggleAvailability availability,
        Func<DeliveryOutcome> toggle,
        Func<DeliveryOutcome> invoke)
    {
        var toggleRun = Chain.Gated(availability.ToggleOk, toggle);
        var invokeRun = Chain.Gated(availability.InvokeOk, invoke);
        return Chain.ExecuteChain(
            deadline,
            ToggleChainDef,
            policy,
            new[]
            {
                new ChainRung(ToggleLabel, false, toggleRun),
                new ChainRung(Chain.InvokeLabel, false, invokeRun),
            });
    }

    /// Injected Check/Uncheck: unit-test seam and live path.
    internal static List<ActionStep> CheckUncheckJudgedFor(
        Deadline deadline,
        CheckUncheckPlan plan,
        Func<ToggleKind?> readState,
        Func<bool> toggle,
        Func<bool> invoke)
    {
        var wantChecked = plan.WantChecked;
        deadline.EnsureRemaining();
        var before = readState();
        if (before is { } known && MatchesChecked(known, wantChecked))
        {
            return new List<ActionStep> { Chain.BuildStep(Chain.AlreadyLabel, DeliveryOutcome.SatisfiedNoDelivery) };
        }

        var steps = new List<ActionStep>();
        if (plan.ToggleOk)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                deadline.EnsureRemaining();
                if (!toggle())
                {
                    break;
                }

                var last = attempt == 1;
                var verified = Chain.AfterDeliveryOf(() =>
                    PollChecked(wantChecked, deadline, readState, last ? null : before));
                steps.Add(Chain.BuildStep(ToggleLabel, DeliveryOutcomeRules.FromDelivery(true, verified)));
                if (verified || !ObservedAChange(before, readState()))
                {
                    return steps;
                }
            }
        }

        if (plan.InvokeOk && !Chain.DeliveryOccurred(steps))
        {
            deadline.EnsureRemaining();
            if (invoke())
            {
                var verified = Chain.AfterDeliveryOf(() => PollChecked(wantChecked, deadline, readState, null));
                steps.Add(Chain.BuildStep(Chain.InvokeLabel, DeliveryOutcomeRules.FromDelivery(true, verified)));
                return steps;
            }

            steps.Add(Chain.BuildStep(Chain.InvokeLabel, DeliveryOutcome.NotDelivered));
        }

        if (Chain.DeliveryOccurred(steps))
        {
            return steps;
        }

        throw Exhausted();
    }

    private static DeliveryOutcome DeliveredWithObserve(
        ToggleKind? before,
        Deadline deadline,
        Element element,
        Func<Element, bool> deliver)
    {
        if (!deliver(element))
        {
            return DeliveryOutcome.NotDelivered;
        }

        var changed = Chain.AfterDeliveryOf(() => ObserveChange(before, deadline, element));
        return DeliveryOutcomeRules.FromDelivery(true, changed);
    }

    private static bool ToggleAvailable(Element element) =>
        PropertyReader.ReadOne(element, TreeProperty.ToggleAvailable).Flag() == true;

    private static ToggleKind? ReadToggleState(Element element)
    {
        var raw = PropertyReader.ReadOne(element, TreeProperty.ToggleState).Number();
        return raw is { } value ? FromUiaValue(value) : null;
    }

    private static ToggleKind? ReadToggleKind(Element element) =>
        ToggleAvailable(element) ? ReadToggleState(element) : null;

    private static ToggleKind? FromUiaValue(int value) => value switch
    {
        0 => ToggleKind.Off,
        1 => ToggleKind.On,
        2 => ToggleKind.Indeterminate,
        _ => null,
    };

    private static bool MatchesChecked(ToggleKind state, bool wantChecked) =>
        (state, wantChecked) is (ToggleKind.On, true) or (ToggleKind.Off, false);

    private static bool PatternToggle(Element element)
    {
        ITogglePattern pattern;
        try
        {
            pattern = element.GetTogglePattern();
        }
        catch (UiaException error)
        {
            return Mutation.ClassifyWrite("get_pattern", ToggleLabel, error);
        }

        try
        {
            pattern.Toggle();
            return Mutation.ClassifySuccess();
        }
        catch (UiaException error)
        {
            return Mutation.ClassifyWrite("Toggle", ToggleLabel, error);
        }
    }

    /// `before` is only ever set when the caller's own upfront
    /// ToggleAvailable read was true (<see cref="ToggleSteps"/>), so this loop reads
    /// ToggleState directly rather than re-issuing that availability
    /// check on every tick the way <see cref="ReadToggleKind"/> would.
    private static bool ObserveChange(ToggleKind? before, Deadline deadline, Element element)
    {
        if (before is not { } start)
        {
            return false;
        }

        var budget = Chain.CappedVerificationEnd(deadline, ToggleTimeout);
        var clock = Stopwatch.StartNew();
        ToggleKind? candidateValue = null;
        var candidateSince = TimeSpan.Zero;
        while (true)
        {
            if (ReadToggleState(element) is { } current)
            {
                if (current != start)
                {
                    if (candidateValue == current)
                    {
                        if (clock.Elapsed - candidateSince >= ToggleStable)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        candidateValue = current;
                        candidateSince = clock.Elapsed;
                    }
                }
                else
                {
                    candidateValue = null;
                }
            }

            if (DateTime.UtcNow >= budget)
            {
                return false;
            }

            SleepPoll(deadline);
        }
    }

    /// Whether the control demonstrably moved, which is what licenses a
    /// second toggle.
    ///
    /// A tri-state control needs two toggles to reach checked from
    /// indeterminate, and after the first one the state has visibly changed:
    /// the delivery is proven, and the second toggle continues toward the
    /// target. A state that reads the same as it did before is the opposite
    /// case: either the toggle did not land, or it landed and this provider
    /// is not reporting it. Toggling again on that ambiguity is the one move
    /// that can end with the control back where it started while the step
    /// reports delivery, so the unverified delivery is returned instead and
    /// the caller re-reads.
    private static bool ObservedAChange(ToggleKind? before, ToggleKind? after) =>
        before is not null && after is not null && before != after;

    private static bool PollChecked(
        bool wantChecked,
        Deadline deadline,
        Func<ToggleKind?> readState,
        ToggleKind? earlyExitAfterChangeFrom)
    {
        var budget = Chain.CappedVerificationEnd(deadline, ToggleTimeout);
        while (true)
        {
            var state = readState();
            if (state is { } known && MatchesChecked(known, wantChecked))
            {
                return true;
            }

            if (state is { } changed && earlyExitAfterChangeFrom is { } before && changed != before)
            {
                SleepPoll(deadline);
                return readState() is { } settled && MatchesChecked(settled, wantChecked);
            }

            if (DateTime.UtcNow >= budget)
            {
                return false;
            }

            SleepPoll(deadline);
        }
    }

    private static void SleepPoll(Deadline deadline) =>
        Thread.Sleep(deadline.RemainingSlice(PollSlice));

    private static AdapterError Exhausted() =>
        Chain.ExhaustedError(ErrorCode.ActionFailed, "All chain steps exhausted", CheckSuggestion);
}
