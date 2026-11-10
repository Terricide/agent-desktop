using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace AgentDesktop.Windows.Actions;

/// <summary>
/// Bounded SelectionItem descendant search and scroll-to-realize.
/// The budget, scroll-loop and duplicate-match rules are complete; UIA reads are seams.
/// </summary>
internal static class SelectSearch
{
    internal const int MaxSelectNodes = 2048;
    internal const byte MaxSelectDepth = 8;
    internal const uint MaxRealizeScrolls = 16;
    internal const string ScrollLabel = "scroll";

    internal static bool SelectNodeBudgetHasRoom(int visited, int pending, int staged)
    {
        long total = (long)visited + pending + staged;
        return total < MaxSelectNodes;
    }

    /// <summary>
    /// Runs up to <paramref name="maxSteps"/> realize scrolls. Each step returns
    /// (Stop, EndOfRange): a stop ends the loop without a search; otherwise a search
    /// runs after the scroll, and an end of range ends the loop after that search.
    /// Search after every completed realize scroll so intermediate virtualized
    /// duplicates cannot scroll past the finder unnoticed.
    /// </summary>
    internal static void DriveRealizeScrolls(
        uint maxSteps,
        global::System.Func<(bool Stop, bool EndOfRange)> next,
        global::System.Action afterScroll)
    {
        for (uint step = 0; step < maxSteps; step++)
        {
            (bool stop, bool endOfRange) = next();
            if (stop)
            {
                return;
            }
            afterScroll();
            if (endOfRange)
            {
                return;
            }
        }
    }

    /// <summary>
    /// A second same-name SelectionItem is <c>AMBIGUOUS_TARGET</c>, never a silent pick.
    /// </summary>
    internal static void AcceptSelectionMatch(bool alreadyFound)
    {
        if (alreadyFound)
        {
            throw AmbiguousSelectValue();
        }
    }

    internal static Element? FindNamedSelectionItem(
        Element root,
        string value,
        Deadline deadline,
        Element? prior)
    {
        var source = PrepareRoot(root);
        Element prepared = source.PrepareRoot(root);
        var stack = new List<(Element Node, byte Depth)>();
        PushChildren(source, prepared, 1, deadline, 0, stack);
        int visited = 0;
        Element? found = prior;
        while (stack.Count > 0)
        {
            (Element candidate, byte depth) = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            deadline.EnsureRemaining();
            visited++;
            if (visited > MaxSelectNodes)
            {
                throw NodeBudgetError();
            }
            if (SelectionItemAvailable(candidate) && NameMatches(candidate, value))
            {
                if (found is null)
                {
                    found = candidate;
                }
                else if (!SameElement(found, candidate))
                {
                    AcceptSelectionMatch(true);
                }
            }
            if (depth < MaxSelectDepth)
            {
                PushChildren(source, candidate, (byte)(depth + 1), deadline, visited, stack);
            }
        }
        return found;
    }

    internal static void ScrollToRealize(
        Element element,
        Deadline deadline,
        global::System.Action afterScroll)
    {
        if (!ScrollAvailable(element))
        {
            return;
        }
        if (!IsVerticallyScrollable(element))
        {
            return;
        }
        DriveRealizeScrolls(
            MaxRealizeScrolls,
            () =>
            {
                deadline.EnsureRemaining();
                double? before = ReadVerticalScrollPercent(element);
                if (!TryScrollSmallIncrement(element, out AdapterError? failure))
                {
                    ClassifyWrite("Scroll", ScrollLabel, failure);
                    return (true, false);
                }
                ClassifySuccess();
                double? after = ReadVerticalScrollPercent(element);
                return (false, before.HasValue && before == after);
            },
            afterScroll);
    }

    internal static bool SelectionItemAvailable(Element element)
    {
        return ReadFlag(element, TreeProperty.SelectionItemAvailable) == true;
    }

    /// <summary>
    /// Matches the element's resolved accessible name against <paramref name="value"/>
    /// with ASCII case folding. An unknown name never matches.
    /// </summary>
    internal static bool NameMatches(Element element, string value)
    {
        string? name = ResolveKnownName(element);
        return name is not null && EqualsAsciiIgnoreCase(name, value);
    }

    /// <summary>
    /// Mirrors Rust <c>eq_ignore_ascii_case</c>: only ASCII letters fold; every other
    /// character must match exactly.
    /// </summary>
    internal static bool EqualsAsciiIgnoreCase(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (int index = 0; index < left.Length; index++)
        {
            if (FoldAscii(left[index]) != FoldAscii(right[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static char FoldAscii(char character)
    {
        return character is >= 'A' and <= 'Z' ? (char)(character + ('a' - 'A')) : character;
    }

    private static void PushChildren(
        UiaTreeSource source,
        Element parent,
        byte childDepth,
        Deadline deadline,
        int visited,
        List<(Element Node, byte Depth)> stack)
    {
        deadline.EnsureRemaining();
        var children = new List<Element>();
        var hr = source.Children(parent, false, children);
        if (hr < 0)
        {
            throw AutomationClassify.UiaFailureError(AutomationClassify.Failure.Hresult(hr),
                "enumerate children for select");
        }

        for (var index = children.Count - 1; index >= 0; index--)
        {
            if (!SelectNodeBudgetHasRoom(visited, stack.Count, index))
            {
                throw NodeBudgetError();
            }

            deadline.EnsureRemaining();
            stack.Add((children[index], childDepth));
        }
    }

    /// <summary>
    /// The node-budget failure: an <c>APP_UNRESPONSIVE</c> error marked incomplete.
    /// </summary>
    private static AdapterError NodeBudgetError()
    {
        return new AdapterError(
                ErrorCode.AppUnresponsive,
                "Select search exceeded its accessibility-node budget")
            .WithDetails(new JsonObject
            {
                ["kind"] = "select_node_limit",
                ["limit"] = MaxSelectNodes,
                ["complete"] = false,
            });
    }

    /// <summary>
    /// The duplicate-match failure: an <c>AMBIGUOUS_TARGET</c> error carrying its kind.
    /// </summary>
    private static AdapterError AmbiguousSelectValue()
    {
        return new AdapterError(
                ErrorCode.AmbiguousTarget,
                "Multiple SelectionItem elements share the requested accessible name")
            .WithDetails(new JsonObject { ["kind"] = "ambiguous_select_value" });
    }

    /// <summary>The source the walk enumerates through, built from the root, with the root prepared
    /// for it. A cached source stamps the root's provenance so a web wrapper is recognised; a live
    /// source returns the root unchanged.</summary>
    private static UiaTreeSource PrepareRoot(Element root)
    {
        var source = UiaTreeSource.ForRoot(root);
        source.PrepareRoot(root);
        return source;
    }

    /// <summary>Whether two refs address the same element, read through the client's own comparison
    /// rather than by a field that two elements can share. A comparison that failed is not read as
    /// "same".</summary>
    private static bool SameElement(Element left, Element right)
    {
        if (Client.Create(out var client) < 0 || client is null)
        {
            return false;
        }

        using (client)
        {
            return client.CompareElements(left.Pointer, right.Pointer, out var same) >= 0 && same;
        }
    }

    /// <summary>The window's scroll affordance, from the property read that says whether a scroll
    /// pattern exists at all.</summary>
    private static bool ScrollAvailable(Element element) =>
        PropertyReader.ReadOne(element, TreeProperty.ScrollAvailable).Flag() == true;

    /// <summary>The vertical axis of the scroll pattern. A pattern the provider does not publish, or a
    /// read that failed, answers false, which is the path the Rust unwrap_or(false) takes.</summary>
    private static bool IsVerticallyScrollable(Element element) =>
        TryPattern(element)?.IsVerticallyScrollable() == true;

    /// <summary>The scroll pattern, or null when the provider does not publish it.</summary>
    private static Scroll.IScrollPattern? TryPattern(Element element)
    {
        try
        {
            return element.GetScrollPattern();
        }
        catch (AdapterError)
        {
            return null;
        }
    }
    /// <summary>The vertical scroll percent, or null when the pattern is absent or the read failed.</summary>
    private static double? ReadVerticalScrollPercent(Element element)
    {
        var pattern = TryPattern(element);
        if (pattern is null)
        {
            return null;
        }

        try
        {
            return pattern.VerticalScrollPercent();
        }
        catch (AdapterError)
        {
            return null;
        }
    }

    /// <summary>One small vertical increment. A pattern the provider does not publish, or a call that
    /// fails, reports false and hands the structured failure to <see cref="ClassifyWrite"/>.</summary>
    private static bool TryScrollSmallIncrement(Element element, out AdapterError? failure)
    {
        failure = null;
        var pattern = TryPattern(element);
        if (pattern is null)
        {
            failure = AdapterError.FromCode(ErrorCode.ActionNotSupported,
                "the element publishes no ScrollPattern");
            return false;
        }

        try
        {
            pattern.Scroll(Scroll.ScrollAmount.NoAmount, Scroll.ScrollAmount.SmallIncrement);
            return true;
        }
        catch (AdapterError error)
        {
            failure = error;
            return false;
        }
    }

    /// <summary>A clean write is the delivered mutation outcome.</summary>
    private static void ClassifySuccess() => Mutation.ClassifySuccess();

    /// <summary>Classifies a failed scroll write. The pattern call already produced the structured
    /// error for its failure, so the write is not delivered and the error is thrown.</summary>
    private static void ClassifyWrite(string action, string label, AdapterError? failure)
    {
        if (failure is null)
        {
            return;
        }

        throw failure;
    }

    /// <summary>A property read in its flag view. A read that never happened is null, not false.</summary>
    private static bool? ReadFlag(Element element, TreeProperty property) =>
        PropertyReader.ReadOne(element, property).Flag();

    /// <summary>The known accessible name, built from the Name, FullDescription, HelpText and IsPassword
    /// reads with the uncached label read, or null when the name is not known.</summary>
    private static string? ResolveKnownName(Element element) =>
        UiaTreeSource.ReadLabelLive(element).Text;
}
