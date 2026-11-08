using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>SelectionItemPattern selection with bounded descendant search and collapsed-container
/// expansion. Mirrors crates/windows/src/actions/select.rs and select_search.rs.</summary>
internal static class SelectAction
{
    internal const string SelectLabel = "SelectionItemPattern.Select";

    private const int MaxSelectNodes = 2_048;

    private const int MaxSelectDepth = 8;

    private const int VerifyTimeoutMs = 600;

    private const int PollSliceMs = 25;

    internal static List<ActionStep> SelectSteps(Element element, string value, Deadline deadline)
    {
        var selfMatch = SelectionItemAvailable(element) && NameMatches(element, value);
        var needsExpand = IsCollapsedContainer(element);
        var expanded = false;
        Element? matched = null;
        try
        {
            if (deadline.IsExpired())
            {
                throw WindowRoot.DeadlineExceeded(deadline);
            }

            if (selfMatch)
            {
                var outcome = DeliveredSelect(element, element, value, deadline);
                return new List<ActionStep> { Chain.BuildStep(SelectLabel, outcome) };
            }

            if (needsExpand)
            {
                ExpandContainer(element);
                expanded = true;
            }

            matched = FindNamedSelectionItem(element, value, deadline);
            if (matched is null)
            {
                if (expanded)
                {
                    CollapseContainer(element);
                }

                throw NotFoundChars(value.EnumerateRunes().Count());
            }

            var selectOutcome = DeliveredSelect(element, matched, value, deadline);
            return new List<ActionStep> { Chain.BuildStep(SelectLabel, selectOutcome) };
        }
        catch
        {
            if (expanded)
            {
                CollapseContainer(element);
            }

            throw;
        }
        finally
        {
            matched?.Dispose();
        }
    }

    internal static bool? ResolveSelectVerification(bool? hasContainerValue, bool? containerMatched, bool? isSelected) =>
        hasContainerValue == true && containerMatched.HasValue ? containerMatched : isSelected;

    private static DeliveryOutcome DeliveredSelect(
        Element container,
        Element target,
        string value,
        Deadline deadline)
    {
        var delivered = Chain.InvokeVoidPattern(
            target,
            Slots.PatternSelectionItem,
            Slots.SelectionItemPatternSelect,
            "Select",
            SelectLabel);
        if (!delivered)
        {
            return DeliveryOutcome.NotDelivered;
        }

        var containerValueAvailable = container.CurrentProperty(PropertyIds.IsValuePatternAvailable).Value is true;
        bool? lastVerified = null;
        Chain.PollVerify(deadline, VerifyTimeoutMs, PollSliceMs, () =>
        {
            var containerMatch = containerValueAvailable
                ? ValueWrite.GatedPatternValueEquals(container, value)
                : null;
            var isSelected = target.CurrentProperty(PropertyIds.SelectionItemIsSelected).Value as bool?;
            lastVerified = ResolveSelectVerification(containerValueAvailable, containerMatch, isSelected);
            return lastVerified == true;
        });

        return Chain.FromObservation(lastVerified);
    }

    private static Element? FindNamedSelectionItem(Element container, string value, Deadline deadline)
    {
        var hr = Client.Create(out var client);
        if (client is null)
        {
            throw UiaFailure.Error(hr, "create a UI Automation client for selection item search");
        }

        using (client)
        {
            var walkerHr = client.RawViewWalker(out var walker);
            if (walkerHr < 0 || walker == IntPtr.Zero)
            {
                Calls.Release(walker);
                throw UiaFailure.Error(walkerHr, "create a raw view walker for selection item search");
            }

            var matches = new List<Element>();
            var visited = 0;
            try
            {
                WalkDescendants(client, walker, container, value, 1, ref visited, matches, deadline);
            }
            catch
            {
                foreach (var match in matches)
                {
                    match.Dispose();
                }

                Calls.Release(walker);
                throw;
            }

            Calls.Release(walker);
            if (matches.Count == 0)
            {
                return null;
            }

            if (matches.Count > 1)
            {
                var count = matches.Count;
                foreach (var match in matches)
                {
                    match.Dispose();
                }

                throw new AdapterError(
                    ErrorCode.AmbiguousTarget,
                    $"Multiple selection items matched the requested value ({value.EnumerateRunes().Count()} chars)")
                    .WithDetails(new JsonObject { ["matches"] = count })
                    .WithDisposition(DeliverySemantics.NotDelivered())
                    .WithSuggestion("Target the specific item ref from a snapshot instead of selecting by container text.");
            }

            return matches[0];
        }
    }

    private static void WalkDescendants(
        Client client,
        IntPtr walker,
        Element parent,
        string value,
        int depth,
        ref int visited,
        List<Element> matches,
        Deadline deadline)
    {
        if (depth > MaxSelectDepth || visited >= MaxSelectNodes)
        {
            return;
        }

        if (Calls.CallOutPointerWithArg(walker, Slots.WalkerGetFirstChild, parent.Pointer, out var current) < 0)
        {
            return;
        }

        while (current != IntPtr.Zero)
        {
            if (deadline.IsExpired())
            {
                Calls.Release(current);
                throw WindowRoot.DeadlineExceeded(deadline);
            }

            visited++;
            var child = new Element(current);
            var isMatch = SelectionItemAvailable(child) && NameMatches(child, value);
            if (visited < MaxSelectNodes && depth < MaxSelectDepth)
            {
                WalkDescendants(client, walker, child, value, depth + 1, ref visited, matches, deadline);
            }

            var nextHr = Calls.CallOutPointerWithArg(walker, Slots.WalkerGetNextSibling, child.Pointer, out var next);
            if (isMatch)
            {
                if (!ContainsSameElement(client, matches, child))
                {
                    matches.Add(child);
                }
                else
                {
                    child.Dispose();
                }
            }
            else
            {
                child.Dispose();
            }

            if (nextHr < 0 || visited >= MaxSelectNodes)
            {
                Calls.Release(next);
                break;
            }

            current = next;
        }
    }

    private static bool ContainsSameElement(Client client, List<Element> existing, Element candidate) =>
        existing.Any(item => client.CompareElements(item.Pointer, candidate.Pointer, out var same) >= 0 && same);

    private static bool SelectionItemAvailable(Element element) =>
        element.CurrentProperty(PropertyIds.IsSelectionItemPatternAvailable).Value is true;

    private static bool NameMatches(Element element, string value) =>
        !NodeProperties.IsLivePassword(element)
        && element.CurrentName() is { Kind: LocatorFieldKind.Known, Value: var name }
        && name == value;

    private static bool IsCollapsedContainer(Element element) =>
        element.CurrentProperty(PropertyIds.IsExpandCollapsePatternAvailable).Value is true
        && element.CurrentProperty(PropertyIds.ExpandCollapseState).Value is 0;

    private static void ExpandContainer(Element element) =>
        Chain.InvokeVoidPattern(
            element,
            Slots.PatternExpandCollapse,
            Slots.ExpandCollapsePatternExpand,
            "Expand",
            ToggleAndDisclosure.ExpandLabel);

    private static void CollapseContainer(Element element)
    {
        if (element.CurrentPattern(Slots.PatternExpandCollapse, out var pattern) >= 0 && pattern != IntPtr.Zero)
        {
            Calls.CallVoid(pattern, Slots.ExpandCollapsePatternCollapse);
        }

        Calls.Release(pattern);
    }

    private static AdapterError NotFoundChars(int chars) =>
        new AdapterError(ErrorCode.ElementNotFound, $"No selection item matched the requested value ({chars} chars)")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithSuggestion("Use find or snapshot to inspect the available selection items.");
}
