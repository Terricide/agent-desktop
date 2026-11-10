using System;
using System.Collections.Generic;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>
/// Expand / Collapse via ExpandCollapsePattern with Known-opposite Invoke.
/// LeafNode means the pattern is present but never expands (A19-2): the Expand
/// rung returns not-delivered without invoking, Invoke stays gated on a
/// Known-opposite state, and the chain exhausts honestly.
/// </summary>
internal static class Disclosure
{
    internal const string ExpandLabel = "ExpandCollapsePattern.Expand";

    internal const string CollapseLabel = "ExpandCollapsePattern.Collapse";

    private const long DisclosureTimeoutMs = 200;

    private const int PollSliceMs = 20;

    internal static readonly ChainDef DisclosureChain = new(
        "Refresh the snapshot and retry, or target an expandable container.",
        false);

    internal static List<ActionStep> ExpandSteps(Element element, InteractionPolicy policy, Deadline deadline) =>
        DisclosureSteps(element, true, policy, deadline);

    internal static List<ActionStep> CollapseSteps(Element element, InteractionPolicy policy, Deadline deadline) =>
        DisclosureSteps(element, false, policy, deadline);

    internal static ExpandKind? KindFromInt(int value) => value switch
    {
        0 => ExpandKind.Collapsed,
        1 => ExpandKind.Expanded,
        2 => ExpandKind.PartiallyExpanded,
        3 => ExpandKind.LeafNode,
        _ => null,
    };

    /// <summary>Whether Invoke may fire: only a Known-opposite pre-read (never Unknown or Leaf).</summary>
    internal static bool InvokeAllowed(ExpandKind? current, bool wantExpanded) =>
        current is { } state && IsKnownOpposite(state, wantExpanded);

    /// <summary>Satisfied, leaf and mutable plan from a pre-read (macOS disclosure_plan parity).</summary>
    internal static (bool Satisfied, bool Leaf, bool AllowPattern) DisclosurePlan(ExpandKind? current, bool wantExpanded)
    {
        var satisfied = current is { } state && IsTarget(state, wantExpanded);
        var leaf = current == ExpandKind.LeafNode;
        return (satisfied, leaf, !satisfied && !leaf);
    }

    /// <summary>Injected Expand/Collapse chain: the unit-test seam and the live path.</summary>
    internal static List<ActionStep> DisclosureJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        DisclosureInput input,
        Func<DeliveryOutcome> pattern,
        Func<DeliveryOutcome> invoke)
    {
        var (satisfied, _, allowPattern) = DisclosurePlan(input.Current, input.WantExpanded);
        if (satisfied)
        {
            return new List<ActionStep> { Chain.BuildStep(Chain.AlreadyLabel, DeliveryOutcome.SatisfiedNoDelivery) };
        }

        var label = input.WantExpanded ? ExpandLabel : CollapseLabel;
        var allowInvoke = input.InvokeOk && InvokeAllowed(input.Current, input.WantExpanded);
        var patternRun = Chain.Gated(input.PatternOk && allowPattern, pattern);
        var invokeRun = Chain.Gated(allowInvoke, invoke);
        return Chain.ExecuteChain(
            deadline,
            DisclosureChain,
            policy,
            new[]
            {
                new ChainRung(label, false, patternRun),
                new ChainRung(Chain.InvokeLabel, false, invokeRun),
            });
    }

    private static List<ActionStep> DisclosureSteps(Element element, bool wantExpanded, InteractionPolicy policy, Deadline deadline)
    {
        var patternOk = ExpandCollapseAvailable(element);
        var current = patternOk ? ReadExpandState(element) : null;
        return DisclosureJudgedFor(
            deadline,
            policy,
            new DisclosureInput(wantExpanded, current, patternOk, Chain.InvokeAvailable(element)),
            () => DeliveredWithObserve(element, wantExpanded, deadline, e => PatternExpandCollapse(e, wantExpanded)),
            () => DeliveredWithObserve(element, wantExpanded, deadline, Chain.InvokePatternDelivered));
    }

    private static DeliveryOutcome DeliveredWithObserve(
        Element element,
        bool wantExpanded,
        Deadline deadline,
        Func<Element, bool> deliver)
    {
        if (!deliver(element))
        {
            return DeliveryOutcome.NotDelivered;
        }

        bool reached;
        try
        {
            reached = PollTarget(wantExpanded, deadline, element);
        }
        catch (AdapterError error)
        {
            throw Chain.AfterDelivery(error);
        }

        return Chain.FromDelivery(true, reached);
    }

    private static bool ExpandCollapseAvailable(Element element) =>
        element.CurrentProperty(PropertyIds.IsExpandCollapsePatternAvailable).Value is true;

    private static ExpandKind? ReadExpandState(Element element) =>
        element.CurrentProperty(PropertyIds.ExpandCollapseState).Value is int number
            ? KindFromInt(number)
            : null;

    private static bool PatternExpandCollapse(Element element, bool wantExpanded) =>
        Chain.InvokeVoidPattern(
            element,
            Slots.PatternExpandCollapse,
            wantExpanded ? Slots.ExpandCollapsePatternExpand : Slots.ExpandCollapsePatternCollapse,
            wantExpanded ? "Expand" : "Collapse",
            wantExpanded ? ExpandLabel : CollapseLabel);

    /// <remarks>
    /// Reads <c>ExpandCollapseAvailable</c> once before the loop rather than on every tick: the
    /// pattern's availability cannot legitimately flip mid-verification, so re-reading it every
    /// 20ms over the 200ms window bought nothing.
    /// </remarks>
    private static bool PollTarget(bool wantExpanded, Deadline deadline, Element element)
    {
        deadline.EnsureRemaining();
        var patternOk = ExpandCollapseAvailable(element);
        return Chain.PollVerify(
            deadline,
            DisclosureTimeoutMs,
            PollSliceMs,
            () => patternOk
                && ReadExpandState(element) is { } state
                && IsTarget(state, wantExpanded));
    }

    private static bool IsTarget(ExpandKind kind, bool wantExpanded) =>
        (kind, wantExpanded) is (ExpandKind.Expanded, true) or (ExpandKind.Collapsed, false);

    private static bool IsKnownOpposite(ExpandKind kind, bool wantExpanded) =>
        (kind, wantExpanded) is (ExpandKind.Collapsed, true) or (ExpandKind.Expanded, false);
}
