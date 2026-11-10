using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;
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
        Element prepared = PrepareRoot(root);
        var stack = new List<(Element Node, byte Depth)>();
        PushChildren(prepared, 1, deadline, 0, stack);
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
                PushChildren(candidate, (byte)(depth + 1), deadline, visited, stack);
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
                if (!TryScrollSmallIncrement(element, out global::System.Exception? failure))
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
        Element parent,
        byte childDepth,
        Deadline deadline,
        int visited,
        List<(Element Node, byte Depth)> stack)
    {
        deadline.EnsureRemaining();
        Element? child = FirstChild(parent);
        var siblings = new List<Element>();
        while (child is not null)
        {
            if (!SelectNodeBudgetHasRoom(visited, stack.Count, siblings.Count))
            {
                throw NodeBudgetError();
            }
            deadline.EnsureRemaining();
            Element? next = NextSibling(child);
            siblings.Add(child);
            child = next;
        }
        for (int index = siblings.Count - 1; index >= 0; index--)
        {
            stack.Add((siblings[index], childDepth));
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

    /// <summary>
    /// SEAM: crates/windows/src/tree/walk/walker_source.rs (UiaTreeSource::for_root, prepare_root).
    /// </summary>
    private static Element PrepareRoot(Element root)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/tree/walk/walker_source.rs (first_child). Exhaustion maps to null;
    /// other failures map through uia_failure_error with "enumerate children for select".
    /// </summary>
    private static Element? FirstChild(Element parent)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/tree/walk/walker_source.rs (next_sibling). Exhaustion maps to null;
    /// other failures map through uia_failure_error with "enumerate siblings for select".
    /// </summary>
    private static Element? NextSibling(Element current)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/tree/walk/walker_source.rs and tree/automation.rs (same_element).
    /// </summary>
    private static bool SameElement(Element left, Element right)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/tree/properties (TreeProperty::ScrollAvailable flag read).
    /// </summary>
    private static bool ScrollAvailable(Element element)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: UIScrollPattern lookup and is_vertically_scrollable. A missing pattern or a failed
    /// read returns false, matching the Rust unwrap_or(false) path.
    /// </summary>
    private static bool IsVerticallyScrollable(Element element)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: UIScrollPattern get_vertical_scroll_percent. A failed read returns null.
    /// </summary>
    private static double? ReadVerticalScrollPercent(Element element)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: UIScrollPattern scroll(NoAmount, SmallIncrement). Returns false and exposes the
    /// platform failure for ClassifyWrite when the scroll fails.
    /// </summary>
    private static bool TryScrollSmallIncrement(Element element, out global::System.Exception? failure)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/actions/mutation (classify_success). Throws when classification fails.
    /// </summary>
    private static void ClassifySuccess()
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/actions/mutation (classify_write). Throws the classified error.
    /// </summary>
    private static void ClassifyWrite(string action, string label, global::System.Exception? failure)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/tree/properties (read_one for a TreeProperty, flag view).
    /// </summary>
    private static bool? ReadFlag(Element element, TreeProperty property)
    {
        throw AdapterError.NotSupported("select");
    }

    /// <summary>
    /// SEAM: crates/windows/src/tree/name_evidence.rs and tree/properties/element_properties.rs.
    /// Returns the known accessible name built from the Name, FullDescription, HelpText and
    /// IsPassword reads with read_label(element, false) and name_fields, or null when the
    /// name is not known.
    /// </summary>
    private static string? ResolveKnownName(Element element)
    {
        throw AdapterError.NotSupported("select");
    }
}
