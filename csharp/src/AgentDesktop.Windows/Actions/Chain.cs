using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>Per-rung delivery judgment recorded into an ActionStep, mirroring
/// DeliveryOutcome in crates/windows/src/actions/chain.rs.</summary>
internal enum DeliveryOutcome
{
    NotDelivered,
    SatisfiedNoDelivery,
    DeliveredUnverified,
    DeliveredVerified,
    DeliveredUnobserved,
}

/// <summary>Chain-level configuration shared by every action that runs through the activation chain.</summary>
internal sealed record ChainDef(string Suggestion, bool ContinueAfterUnverifiedDelivery);

/// <summary>One rung in an activation chain whose runner is injected for unit testing and live dispatch.</summary>
internal sealed record ChainRung(string Label, bool RequiresHeaded, Func<DeliveryOutcome> Run);

/// <summary>Activation-chain engine for UIA pattern dispatch, mirroring crates/windows/src/actions/chain.rs.</summary>
internal static class Chain
{
    internal const string InvokeLabel = "InvokePattern.Invoke";

    internal const string LegacyLabel = "LegacyIAccessible.DoDefaultAction";

    internal const string AlreadyLabel = "AlreadyInState";

    internal const string SemanticMechanism = "semantic_api";

    internal static readonly ChainDef ClickChain = new(
        "Refresh the snapshot and retry, or target an element that advertises Invoke or a Legacy default action.",
        false);

    internal static DeliveryOutcome FromDelivery(bool delivered, bool verified) => (delivered, verified) switch
    {
        (false, _) => DeliveryOutcome.NotDelivered,
        (true, false) => DeliveryOutcome.DeliveredUnverified,
        (true, true) => DeliveryOutcome.DeliveredVerified,
    };

    internal static DeliveryOutcome FromObservation(bool? verified) => verified switch
    {
        true => DeliveryOutcome.DeliveredVerified,
        false => DeliveryOutcome.DeliveredUnverified,
        null => DeliveryOutcome.DeliveredUnobserved,
    };

    internal static bool TerminatesChain(this DeliveryOutcome outcome) => outcome != DeliveryOutcome.NotDelivered;

    internal static List<ActionStep> Execute(
        Deadline deadline,
        ChainDef def,
        InteractionPolicy policy,
        IReadOnlyList<ChainRung> rungs)
    {
        var steps = new List<ActionStep>();
        foreach (var rung in rungs)
        {
            if (deadline.IsExpired())
            {
                throw WindowRoot.DeadlineExceeded(deadline).WithDisposition(ExhaustionDisposition(steps));
            }

            if (!RungAllowed(rung, policy))
            {
                continue;
            }

            DeliveryOutcome outcome;
            try
            {
                outcome = rung.Run();
            }
            catch (AdapterError error)
            {
                throw DeliveryOccurred(steps)
                    ? error.WithDisposition(ExhaustionDisposition(steps))
                    : error;
            }

            if (RecordStepOutcome(steps, rung.Label, outcome, def.ContinueAfterUnverifiedDelivery))
            {
                return steps;
            }
        }

        throw new AdapterError(ErrorCode.ActionFailed, "All chain steps exhausted")
            .WithDisposition(ExhaustionDisposition(steps))
            .WithSuggestion(def.Suggestion);
    }

    internal static bool RungAllowed(ChainRung rung, InteractionPolicy policy) =>
        !rung.RequiresHeaded || policy == InteractionPolicy.Physical;

    internal static AdapterError AfterDelivery(AdapterError error) =>
        error.WithDisposition(DeliverySemantics.DeliveredUnverified());

    internal static bool DeliveryOccurred(IReadOnlyList<ActionStep> steps) =>
        steps.Any(step => step.Outcome == "succeeded");

    internal static DeliverySemantics ExhaustionDisposition(IReadOnlyList<ActionStep> steps) =>
        DeliveryOccurred(steps) ? DeliverySemantics.DeliveredUnverified() : DeliverySemantics.NotDelivered();

    internal static ActionStep BuildStep(string label, DeliveryOutcome outcome) => outcome switch
    {
        DeliveryOutcome.NotDelivered => new ActionStep(label, "skipped", SemanticMechanism),
        DeliveryOutcome.SatisfiedNoDelivery => new ActionStep(label, "skipped", SemanticMechanism, true),
        DeliveryOutcome.DeliveredUnobserved => new ActionStep(label, "succeeded", SemanticMechanism),
        DeliveryOutcome.DeliveredUnverified => new ActionStep(label, "succeeded", SemanticMechanism, false),
        DeliveryOutcome.DeliveredVerified => new ActionStep(label, "succeeded", SemanticMechanism, true),
        _ => new ActionStep(label, "skipped", SemanticMechanism),
    };

    internal static bool RecordStepOutcome(
        List<ActionStep> steps,
        string label,
        DeliveryOutcome outcome,
        bool continueAfterUnverifiedDelivery)
    {
        steps.Add(BuildStep(label, outcome));
        return outcome.TerminatesChain()
            && !(continueAfterUnverifiedDelivery && outcome == DeliveryOutcome.DeliveredUnverified);
    }

    internal static Func<DeliveryOutcome> Gated(bool available, Func<DeliveryOutcome> run) =>
        () => available ? run() : DeliveryOutcome.NotDelivered;

    internal static bool InvokeAvailable(Element element) =>
        element.CurrentProperty(PropertyIds.IsInvokePatternAvailable).Value is true;

    internal static bool LegacyAvailable(Element element) =>
        element.CurrentProperty(PropertyIds.LegacyDefaultAction).Value is string text && !string.IsNullOrWhiteSpace(text);

    internal static bool InvokePatternDelivered(Element element) =>
        InvokeVoidPattern(element, Slots.PatternInvoke, Slots.InvokePatternInvoke, "Invoke", InvokeLabel);

    internal static bool LegacyDefaultActionDelivered(Element element) =>
        InvokeVoidPattern(element, Slots.PatternLegacyIAccessible, Slots.LegacyIAccessiblePatternDoDefaultAction, "DoDefaultAction", LegacyLabel);

    internal static bool InvokeVoidPattern(Element element, int patternId, int methodSlot, string action, string mechanism)
    {
        var hr = element.CurrentPattern(patternId, out var pattern);
        if (hr < 0 || pattern == IntPtr.Zero)
        {
            Calls.Release(pattern);
            return ClassifyWriteOrNotDelivered(hr, "get_pattern", mechanism);
        }

        try
        {
            var invokeHr = Calls.CallVoid(pattern, methodSlot);
            if (invokeHr >= 0)
            {
                return true;
            }

            return ClassifyWriteOrNotDelivered(invokeHr, action, mechanism);
        }
        finally
        {
            Calls.Release(pattern);
        }
    }

    /// <summary>Returns false when the write was cleanly refused as not-delivered (so the chain
    /// can try the next rung), or throws the classified AdapterError on hard/uncertain failure.</summary>
    internal static bool ClassifyWriteOrNotDelivered(int hresult, string action, string mechanism)
    {
        if (UiaFailure.IsPatternAbsent(hresult))
        {
            return false;
        }

        throw UiaFailure.WriteError(hresult, action, mechanism);
    }

    /// <summary>Polls until verify returns true or the verification window (capped by the caller
    /// deadline) expires.</summary>
    internal static bool PollVerify(Deadline deadline, long capMs, int intervalMs, Func<bool> verify)
    {
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        var budgetMs = Math.Max(0, Math.Min(deadline.RemainingMs(), capMs));
        var pollDeadline = Deadline.DetachedAfter(budgetMs);
        while (true)
        {
            if (verify())
            {
                return true;
            }

            if (pollDeadline.IsExpired() || deadline.IsExpired())
            {
                return false;
            }

            Thread.Sleep((int)Math.Max(1, Math.Min(intervalMs, pollDeadline.RemainingMs())));
        }
    }
}

