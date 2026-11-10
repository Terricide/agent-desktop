using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>
/// SelectionItemPattern select with bounded descendant search (A19-7).
/// Self-match uses SelectionItem on the target; otherwise a walker DFS finds a named SelectionItem.
/// Collapsed ExpandCollapse containers expand first and best-effort collapse after failure.
/// Descendant search always scroll-to-realizes (A18-1), re-searching after every scroll step and
/// retaining the first match so an intermediate or below-fold duplicate still surfaces as
/// AMBIGUOUS_TARGET. Container Value verification routes through the IsPassword-gated value helpers.
/// </summary>
internal static class Select
{
    internal const string Label = "SelectionItemPattern.Select";

    internal static readonly TimeSpan VerifyTimeout = TimeSpan.FromMilliseconds(600);

    internal static readonly TimeSpan PollSlice = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Pure verification precedence: container Value outranks <paramref name="isSelected"/> when the gate
    /// produced a comparison; a withheld or absent Value falls back to <paramref name="isSelected"/>.
    /// </summary>
    internal static bool? ResolveVerification(bool? containerValue, bool? isSelected) =>
        containerValue ?? isSelected;

    internal static IReadOnlyList<ActionStep> SelectSteps(Element element, string value, Deadline deadline)
    {
        var selfMatch = SelectSearch.SelectionItemAvailable(element) && SelectSearch.NameMatches(element, value);
        var needsExpand = IsCollapsedContainer(element);
        var expanded = false;
        Element? found = selfMatch ? element : null;

        var plan = new SelectPlan(selfMatch, needsExpand, CountChars(value));
        var ops = new SelectOps(
            Expand: () =>
            {
                deadline.EnsureRemaining();
                ExpandContainer(element);
                expanded = true;
            },
            Collapse: () =>
            {
                if (expanded)
                {
                    CollapseContainer(element);
                }
            },
            Find: () =>
            {
                var next = SelectSearch.FindNamedSelectionItem(element, value, deadline, found);
                if (next is null)
                {
                    return false;
                }
                found = next;
                return true;
            },
            Realize: () =>
            {
                deadline.EnsureRemaining();
                SelectSearch.ScrollToRealize(element, deadline, () =>
                {
                    var next = SelectSearch.FindNamedSelectionItem(element, value, deadline, found);
                    if (next is not null)
                    {
                        found = next;
                    }
                });
            },
            SelectItem: () =>
            {
                if (found is null)
                {
                    throw NotFound(plan.ValueChars);
                }
                return DeliveredSelect(element, found, value, deadline);
            });

        return SelectJudgedFor(deadline, plan, ops);
    }

    /// <summary>
    /// Drives the select sequence: self-match short-circuits, otherwise expand, search, realize, search again,
    /// then select. Collapse runs after any failure past the expansion point.
    /// </summary>
    internal static IReadOnlyList<ActionStep> SelectJudgedFor(Deadline deadline, SelectPlan plan, SelectOps ops)
    {
        deadline.EnsureRemaining();
        if (plan.SelfMatch)
        {
            return SelectItemOrCollapse(ops);
        }
        if (plan.NeedsExpand)
        {
            ops.Expand();
        }
        FindOrCollapse(ops);
        RealizeOrCollapse(ops);
        var found = FindOrCollapse(ops);
        if (!found)
        {
            ops.Collapse();
            throw NotFound(plan.ValueChars);
        }
        return SelectItemOrCollapse(ops);
    }

    private static IReadOnlyList<ActionStep> SelectItemOrCollapse(SelectOps ops)
    {
        try
        {
            return new[] { Chain.BuildStep(Label, ops.SelectItem()) };
        }
        catch (AdapterError)
        {
            ops.Collapse();
            throw;
        }
    }

    private static bool FindOrCollapse(SelectOps ops)
    {
        try
        {
            return ops.Find();
        }
        catch (AdapterError)
        {
            ops.Collapse();
            throw;
        }
    }

    private static void RealizeOrCollapse(SelectOps ops)
    {
        try
        {
            ops.Realize();
        }
        catch (AdapterError)
        {
            ops.Collapse();
            throw;
        }
    }

    private static DeliveryOutcome DeliveredSelect(Element container, Element target, string value, Deadline deadline)
    {
        bool delivered;
        try
        {
            var pattern = target.GetPattern<ISelectionItemPattern>();
            try
            {
                pattern.Select();
                delivered = Mutation.ClassifySuccess();
            }
            catch (UiaException error)
            {
                delivered = Mutation.ClassifyWrite("Select", Label, error);
            }
        }
        catch (UiaException error)
        {
            delivered = Mutation.ClassifyWrite("get_pattern", Label, error);
        }

        if (!delivered)
        {
            return DeliveryOutcome.NotDelivered;
        }

        bool? verified;
        try
        {
            verified = PollVerified(container, target, value, deadline);
        }
        catch (AdapterError error)
        {
            throw Chain.AfterDelivery(error);
        }
        return DeliveryOutcomeRules.FromObservation(verified);
    }

    /// <remarks>
    /// Reads container <c>ValueAvailable</c> once before the loop rather than on every tick: it cannot
    /// legitimately flip mid-verification, so re-checking it every 25ms bought nothing.
    /// </remarks>
    private static bool? PollVerified(Element container, Element target, string value, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var end = Chain.CappedVerificationEnd(deadline, VerifyTimeout);
        var containerValueAvailable = ValueAvailable(container);
        while (true)
        {
            var verified = VerifyOnce(container, target, value, containerValueAvailable);
            if (verified == true)
            {
                return true;
            }
            if (DateTimeOffset.UtcNow >= end)
            {
                return verified;
            }
            Thread.Sleep(deadline.RemainingSlice(PollSlice));
        }
    }

    private static bool? VerifyOnce(Element container, Element target, string value, bool containerValueAvailable)
    {
        bool? containerValue = containerValueAvailable
            ? ValueWrite.GatedPatternValueEquals(container, value)
            : null;
        var selected = PropertyReader.ReadOne(target, TreeProperty.SelectionItemIsSelected).Flag();
        return ResolveVerification(containerValue, selected);
    }

    private static void ExpandContainer(Element element)
    {
        try
        {
            var pattern = element.GetPattern<IExpandCollapsePattern>();
            try
            {
                pattern.Expand();
            }
            catch (UiaException error)
            {
                _ = Mutation.ClassifyWrite("Expand", Disclosure.ExpandLabel, error);
            }
        }
        catch (UiaException error)
        {
            _ = Mutation.ClassifyWrite("get_pattern", Disclosure.ExpandLabel, error);
        }
    }

    private static void CollapseContainer(Element element)
    {
        try
        {
            var pattern = element.GetPattern<IExpandCollapsePattern>();
            try
            {
                pattern.Collapse();
            }
            catch (UiaException)
            {
            }
        }
        catch (UiaException)
        {
        }
    }

    private static bool IsCollapsedContainer(Element element)
    {
        if (PropertyReader.ReadOne(element, TreeProperty.ExpandCollapseAvailable).Flag() != true)
        {
            return false;
        }
        var state = PropertyReader.ReadOne(element, TreeProperty.ExpandCollapseState).Number();
        return state is int code && Disclosure.KindFromInt(code) == ExpandKind.Collapsed;
    }

    private static bool ValueAvailable(Element element) =>
        PropertyReader.ReadOne(element, TreeProperty.ValueAvailable).Flag() == true;

    private static int CountChars(string value) => value.EnumerateRunes().Count();

    private static AdapterError NotFound(int chars) =>
        new AdapterError(
            ErrorCode.ElementNotFound,
            $"No selection item matched the requested value ({chars} chars)")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithSuggestion("Use find or snapshot to inspect the available selection items.");

    /// <summary>
    /// Injected select plan flags: the unit-test seam and the live path share this record.
    /// </summary>
    internal sealed record SelectPlan(bool SelfMatch, bool NeedsExpand, int ValueChars);

    /// <summary>
    /// Injected select operations: the unit-test seam and the live path share this record.
    /// </summary>
    internal sealed record SelectOps(
        global::System.Action Expand,
        global::System.Action Collapse,
        Func<bool> Find,
        global::System.Action Realize,
        Func<DeliveryOutcome> SelectItem);
}
