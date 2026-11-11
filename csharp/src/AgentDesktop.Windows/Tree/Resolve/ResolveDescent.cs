using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Tree.Resolve;

/// <summary>What a failed enumeration step means to the resolver that asked for it.</summary>
internal enum DescentVerdict
{
    /// <summary>The node enumerates nothing further and that is a real answer: keep what was
    /// collected, and the list still counts as whole.</summary>
    Settled,

    /// <summary>The failure is the attempt's answer and propagates as an error.</summary>
    Surfaced,
}

/// <summary>The decision that differs per enumeration axis, with the shape-only phrase each
/// axis reports a surfaced failure under.</summary>
internal sealed record DescentPolicy(
    Func<AutomationClassify.Failure, DescentVerdict> Classify,
    string DescendContext,
    string SiblingContext);

/// <summary>The bounded sibling enumeration and child-index path descent the locator anchor
/// drives. Both primitives are the same enumeration under the same sibling cap and the same
/// deadline, and the loop is written once. The loop consults the deadline on every item,
/// mirroring the element walk's own enumeration: checking it only around the enumeration would
/// let a pathological list run the whole sibling cap after the budget was already spent.</summary>
internal static class ResolveDescent
{
    /// <summary>Enumerates one element's children, honouring the sibling cap as a hard bound on
    /// pathological lists and the deadline as a bound on time. <paramref name="take"/> stops the
    /// loop after that many siblings: null enumerates the whole list, while a path step passes
    /// the wanted index plus one, so reaching the wanted index costs index-plus-one reads however
    /// long the list runs.
    ///
    /// The cap is the other place this enumeration is not literal: a cap-hit keeps the truncated
    /// list and answers it rather than erroring. That is deliberate. The bound exists to make the
    /// loop total against a sibling chain that never terminates - the ancestor cycle guard cannot
    /// see such a chain, because it revisits no element on any root-to-node path - not to clamp a
    /// large but finite list. Measured breadth is nowhere near it: the widest node in the probe
    /// corpus sits in a virtualized Explorer file list whose entire tree over %WINDIR%\System32
    /// is 196 nodes (A1-1), and a WinForms ListBox exposes no items at all to a COM client
    /// (A17-2).
    ///
    /// Surfacing the truncation would cost more honesty than it buys: the bound is shared with
    /// the walk that issues refs in the first place, and that walk enumerates under the same
    /// sibling cap and refuses to present a cap-cut child list as whole, so no stored ref can
    /// name an element past this point in its parent's sibling list. One oversized list anywhere
    /// in a window would otherwise convert every correct settled miss in that window into a retry
    /// loop that re-truncates identically on every attempt and expires as TIMEOUT.
    ///
    /// The caller owns every node returned. Nodes enumerated beyond the cap, or after a deadline
    /// or failure, are disposed here.</summary>
    internal static List<TNode> ReadChildren<TNode>(
        TreeSource<TNode> source,
        TNode element,
        WalkBudget budget,
        DescentPolicy policy,
        int? take)
        where TNode : class, IDisposable
    {
        var limit = take is { } wanted ? Math.Min(wanted, budget.MaxSiblings) : budget.MaxSiblings;
        var enumerated = new List<TNode>();
        if (TreeSourceChildren.Collect(source, element, enumerated).Fault is { } failure)
        {
            if (policy.Classify(failure) == DescentVerdict.Surfaced)
            {
                DisposeFrom(enumerated, 0);
                throw AutomationClassify.UiaFailureError(failure, policy.DescendContext);
            }
        }

        var collected = new List<TNode>();
        try
        {
            for (var index = 0; index < enumerated.Count; index++)
            {
                if (collected.Count >= limit)
                {
                    DisposeFrom(enumerated, index);
                    break;
                }

                budget.Deadline.EnsureRemaining();
                collected.Add(enumerated[index]);
            }
        }
        catch (AdapterError)
        {
            DisposeFrom(enumerated, collected.Count);
            DisposeFrom(collected, 0);
            throw;
        }

        return collected;
    }

    /// <summary>Walks the stored child-index path from a root, O(depth) child reads. Each level
    /// enumerates only to the wanted index plus one and stops: a path step needs the one child it
    /// names, never the thousand siblings after it (a ref to a subkey near the top of an expanded
    /// registry hive would otherwise read all of the hive's thousands of subkeys first).
    ///
    /// The landing is trustworthy on its own terms. ReadChildren only ever appends, and every
    /// exit it can take - the sibling cap, an expired deadline, a failed read - stops the loop
    /// rather than skipping an entry, so the list it returns is always a prefix of the node's
    /// real child list. An index that prefix reaches therefore names the same child the whole
    /// list would name at that index, and this function descends only through indices the prefix
    /// reached: a stored index past a truncation yields null and lands nowhere. What that absence
    /// means belongs to the caller, not here. The index is taken against the raw enumeration, the
    /// same space the walk that issued the ref recorded its stored index in, so a walk that
    /// omitted a sibling from its own output still leaves the stored index pointing at the child
    /// it named.
    ///
    /// Ownership: an empty path returns <paramref name="root"/> itself, still borrowed, so the
    /// caller keeps it. A non-empty path returns a node the caller owns; every intermediate node
    /// and every sibling skipped at each level is disposed here.</summary>
    internal static TNode? DescendPath<TNode>(
        TreeSource<TNode> source,
        TNode root,
        IReadOnlyList<int> path,
        WalkBudget budget,
        DescentPolicy policy)
        where TNode : class, IDisposable
    {
        if (path.Count == 0)
        {
            return root;
        }

        var current = root;
        var owned = false;
        try
        {
            foreach (var index in path)
            {
                var wanted = index < int.MaxValue ? index + 1 : int.MaxValue;
                var read = ReadChildren(source, current, budget, policy, wanted);
                if (owned)
                {
                    current.Dispose();
                    owned = false;
                }

                if (index < 0 || index >= read.Count)
                {
                    DisposeFrom(read, 0);
                    return null;
                }

                var child = read[index];
                read.RemoveAt(index);
                DisposeFrom(read, 0);
                current = child;
                owned = true;
            }
        }
        catch (AdapterError)
        {
            if (owned)
            {
                current.Dispose();
            }

            throw;
        }

        return current;
    }

    private static void DisposeFrom<TNode>(List<TNode> nodes, int from)
        where TNode : class, IDisposable
    {
        for (var index = from; index < nodes.Count; index++)
        {
            nodes[index].Dispose();
        }
    }
}
