using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree;

/// <summary>Which of the two enumeration calls faulted. Typed rather than a string: the two axes behave
/// differently against a dead provider - descent surfaces E_FAIL while the sibling terminator is
/// indistinguishable from end-of-list (A14-4) - so a reader of a trace segment needs to know which one this was.</summary>
internal enum EnumerationAxis
{
    FirstChild,
    NextSibling,
}

/// <summary>The enumeration surface the walk reads from: the live UI Automation tree, or a stub in tests.
/// Every node it hands out belongs to the walk, which disposes it exactly once.</summary>
internal interface EnumerationSource<TNode> where TNode : class, IDisposable
{
    NodeKey Identity(TNode node);

    (ElementProperties Properties, int ReadFailures) Evidence(TNode node);

    bool IsWebWrapper(TNode node, ElementProperties properties);

    (TNode? Element, AutomationClassify.Failure? Failure) FirstChild(TNode node);

    (TNode? Element, AutomationClassify.Failure? Failure) NextSibling(TNode node);

    bool SameElement(TNode ancestor, TNode node);
}

/// <summary>One visited node: its read set, the children the walk kept, whether its subtree was fully observed,
/// and the child count a boundary node knows. Stands in for core's observed subtree until that type is ported.</summary>
internal sealed record WalkSubtree(ElementProperties Evidence, List<WalkSubtree> Children, bool IsComplete, uint? ChildrenCount)
{
    internal bool IsWebWrapper { get; init; }

    internal int SourceChildIndex { get; init; }

    internal bool PredecessorsComplete { get; init; }
}

/// <summary>Traversal accounting for one walk. Stands in for core's traversal stats until that type is ported.</summary>
internal sealed class TraversalCounts
{
    internal int CyclesSkipped;
    internal int WebWrapperNodes;
    internal int NodesVisited;
    internal int DepthHits;
    internal int ChildHits;
    internal byte MaxLogicalDepth;
    internal byte MaxRawDepth;
}

/// <summary>Read-health accounting for one walk. Stands in for core's read stats until that type is ported.</summary>
internal sealed class ReadCounts
{
    internal int ObservationAttempts;
    internal int NativeReadFailures;
    internal int CannotComplete;
    internal int DeadlineExhausted;
}

/// <summary>Everything one walk measured, the accounting that travels with the tree.</summary>
internal sealed record WalkStats(TraversalCounts Traversal, ReadCounts Reads);

/// <summary>The accounting, completeness, and reported faults one enumeration walk finished with.</summary>
internal sealed record EnumerationWalkOutcome(WalkStats Stats, bool Complete, List<AdapterError> Failures);

/// <summary>The traversal state for one walk. The ancestor path is a path, never a global visited set: a global
/// set would prune a real sibling subtree that legitimately re-reaches the same element by a different route.</summary>
internal sealed class TreeWalk<TNode> where TNode : class, IDisposable
{
    /// <summary>How long a boundary node's child-count-only read gets, independent of the walk's own deadline.
    /// A boundary node is read for its count alone, so a lazily materialised child list could otherwise consume an
    /// entire snapshot budget on a count nobody needs that badly. Mirrors macOS's BOUNDARY_COUNT_BUDGET (25ms). The
    /// count stays best-effort: a starved read yields no count while the boundary is still marked truncated.</summary>
    private const int BoundaryCountBudgetMs = 25;

    /// <summary>How many enumeration faults one walk reports. The tree still records every incompleteness; the error
    /// list stays bounded so a trace segment cannot be flooded from a single walk.</summary>
    private const int MaxReportedFailures = 8;

    private readonly EnumerationSource<TNode> _source;
    private readonly WalkBudget _budget;
    private readonly List<(NodeKey Key, TNode Node)> _ancestors = new();
    private readonly WalkStats _stats = new(new TraversalCounts(), new ReadCounts());
    private readonly List<AdapterError> _failures = new();
    private uint _suppressedFailures;
    private bool _complete = true;

    internal TreeWalk(EnumerationSource<TNode> source, WalkBudget budget)
    {
        _source = source;
        _budget = budget;
    }

    /// <summary>How many entries the ancestor path currently holds. Zero on every exit from a completed visit,
    /// including exits taken when enumeration faults.</summary>
    internal int AncestorDepth() => _ancestors.Count;

    /// <summary>Visits one element and everything beneath it. The ancestor entry is pushed once here and popped once
    /// here, so every path out of the visited body leaves the path exactly as it found it.</summary>
    internal WalkSubtree? Visit(TNode node, byte logicalDepth, byte rawDepth)
    {
        if (_budget.Deadline.IsExpired())
        {
            NoteDeadlineExhausted();
            return null;
        }

        var key = _source.Identity(node);
        if (IsAncestor(key, node))
        {
            _stats.Traversal.CyclesSkipped += 1;
            return null;
        }

        _ancestors.Add((key, node));
        try
        {
            return VisitEntered(node, logicalDepth, rawDepth);
        }
        finally
        {
            _ancestors.RemoveAt(_ancestors.Count - 1);
        }
    }

    /// <summary>Reports the walk's accounting, refusing a run that leaked an ancestor entry rather than presenting
    /// its tree as trustworthy.</summary>
    internal EnumerationWalkOutcome Finish()
    {
        if (_ancestors.Count > 0)
        {
            throw AdapterError.FromCode(
                ErrorCode.Internal,
                $"element tree walk retained {_ancestors.Count} ancestor entries");
        }

        if (_failures.Count > 0)
        {
            SuppressedFailureAnnotation.Annotate(_failures[^1], _suppressedFailures);
        }

        return new EnumerationWalkOutcome(_stats, _complete, _failures);
    }

    /// <summary>Visits one node: reads its evidence, resolves subtree depth by wrapper-ness, and reports its children.
    /// A boundary children count is only real when enumeration completed: a deadline- or sibling-cap-cut list has an
    /// unknowable count and marks the node without one.</summary>
    private WalkSubtree? VisitEntered(TNode node, byte logicalDepth, byte rawDepth)
    {
        NoteVisit(logicalDepth, rawDepth);
        var (properties, readFailures) = _source.Evidence(node);
        _stats.Reads.ObservationAttempts += 1;
        _stats.Reads.NativeReadFailures += readFailures;
        var webWrapper = _source.IsWebWrapper(node, properties);
        if (webWrapper)
        {
            _stats.Traversal.WebWrapperNodes += 1;
        }

        var childLogicalDepth = SaturatingAdd(logicalDepth, webWrapper ? 0 : 1);
        if (_budget.Deadline.IsExpired())
        {
            NoteDeadlineExhausted();
            return null;
        }

        var boundaryCandidate = rawDepth >= _budget.MaxRawDepth || childLogicalDepth > _budget.MaxLogicalDepth;
        var readDeadline = boundaryCandidate ? CappedBy(_budget.Deadline, BoundaryCountBudgetMs) : _budget.Deadline;
        var (elements, readComplete) = ReadChildren(node, rawDepth, readDeadline);
        var truncatedByRawDepth = rawDepth >= _budget.MaxRawDepth && elements.Count > 0;
        var atBoundary = truncatedByRawDepth || childLogicalDepth > _budget.MaxLogicalDepth;
        if (!atBoundary)
        {
            var (children, complete) = VisitChildren(elements, readComplete, childLogicalDepth, rawDepth);
            return Finished(new WalkSubtree(properties, children, complete, null), webWrapper, complete);
        }

        if (truncatedByRawDepth)
        {
            _stats.Traversal.DepthHits += 1;
        }

        var count = readComplete && elements.Count > 0 ? (uint?)elements.Count : null;
        DisposeAll(elements);
        var subtreeComplete = readComplete && !truncatedByRawDepth;
        return Finished(new WalkSubtree(properties, new List<WalkSubtree>(), subtreeComplete, count), webWrapper, subtreeComplete);
    }

    private WalkSubtree Finished(WalkSubtree subtree, bool webWrapper, bool subtreeComplete)
    {
        if (!subtreeComplete)
        {
            _complete = false;
        }

        return subtree with { IsWebWrapper = webWrapper };
    }

    /// <summary>The child enumeration loop, written on first-child and next-sibling directly. A single path that
    /// retires end-of-siblings and an RPC fault alike would turn a hung target into a truncated tree that reports
    /// complete. Every failure is classified instead: exhaustion ends the list and leaves the subtree complete,
    /// anything else marks it incomplete and surfaces a structured error. The deadline is the caller's choice: a
    /// boundary node's read is clamped to the boundary count budget because it only ever produces a count.</summary>
    private (List<TNode> Elements, bool Complete) ReadChildren(TNode node, byte rawDepth, Deadline deadline)
    {
        var elements = new List<TNode>();
        var complete = true;
        var (first, firstFailure) = _source.FirstChild(node);
        if (first is null)
        {
            if (firstFailure is { } failure && !failure.IsExhaustion)
            {
                Record(failure, EnumerationAxis.FirstChild, rawDepth, 0);
                complete = false;
            }

            return (elements, complete);
        }

        var current = first;
        while (true)
        {
            if (elements.Count >= _budget.MaxSiblings)
            {
                _stats.Traversal.ChildHits += 1;
                complete = false;
                current.Dispose();
                break;
            }

            if (deadline.IsExpired())
            {
                NoteDeadlineExhausted();
                complete = false;
                current.Dispose();
                break;
            }

            var (sibling, siblingFailure) = _source.NextSibling(current);
            elements.Add(current);
            if (sibling is not null)
            {
                current = sibling;
                continue;
            }

            if (siblingFailure is null || siblingFailure.Value.IsExhaustion)
            {
                break;
            }

            Record(siblingFailure.Value, EnumerationAxis.NextSibling, rawDepth, elements.Count);
            complete = false;
            break;
        }

        return (elements, complete);
    }

    /// <summary>Visits each read child in order. The child index is the child's position in the native list, so a
    /// skipped child leaves a gap that the predecessor marker reports.</summary>
    private (List<WalkSubtree> Children, bool Complete) VisitChildren(List<TNode> elements, bool readComplete, byte childLogicalDepth, byte rawDepth)
    {
        var complete = readComplete;
        var children = new List<WalkSubtree>();
        var predecessorsComplete = true;
        for (var childIndex = 0; childIndex < elements.Count; childIndex++)
        {
            var child = elements[childIndex];
            var subtree = Visit(child, childLogicalDepth, SaturatingAdd(rawDepth, 1));
            child.Dispose();
            if (subtree is null)
            {
                RetainedEdgeCertainty(ref predecessorsComplete, false);
                continue;
            }

            complete &= subtree.IsComplete;
            var edgeComplete = RetainedEdgeCertainty(ref predecessorsComplete, true);
            children.Add(subtree with { SourceChildIndex = childIndex, PredecessorsComplete = edgeComplete });
        }

        return (children, complete);
    }

    private bool IsAncestor(NodeKey key, TNode node)
    {
        if (key is NodeKey.Runtime runtime)
        {
            return _ancestors.Any(entry => entry.Key is NodeKey.Runtime ancestor && runtime.RuntimeIds.SequenceEqual(ancestor.RuntimeIds));
        }

        return _ancestors.Any(entry => _source.SameElement(entry.Node, node));
    }

    private void Record(AutomationClassify.Failure failure, EnumerationAxis axis, byte rawDepth, int childIndex)
    {
        _stats.Reads.CannotComplete += 1;
        if (_failures.Count >= MaxReportedFailures)
        {
            _suppressedFailures += 1;
            return;
        }

        _failures.Add(EnumerationError(failure, axis, rawDepth, childIndex));
    }

    private void NoteVisit(byte logicalDepth, byte rawDepth)
    {
        _stats.Traversal.NodesVisited += 1;
        _stats.Traversal.MaxLogicalDepth = (byte)Math.Max(_stats.Traversal.MaxLogicalDepth, logicalDepth);
        _stats.Traversal.MaxRawDepth = (byte)Math.Max(_stats.Traversal.MaxRawDepth, rawDepth);
    }

    private void NoteDeadlineExhausted()
    {
        _stats.Reads.DeadlineExhausted += 1;
        _complete = false;
    }

    /// <summary>Builds the structured error a failed enumeration surfaces. Shape only: the failure's own HRESULT or
    /// sentinel, which axis faulted, how deep the walk was, and how many siblings had been retired. No name, class
    /// name, property value, or provider description may enter here, because adapter error text is persisted into
    /// session trace segments and the trace export.</summary>
    private static AdapterError EnumerationError(AutomationClassify.Failure failure, EnumerationAxis axis, byte rawDepth, int childIndex)
    {
        var details = new JsonObject
        {
            ["kind"] = "child_enumeration_failed",
            ["axis"] = AxisName(axis),
            ["raw_depth"] = (int)rawDepth,
            ["child_index"] = childIndex,
        };
        return AutomationClassify.UiaFailureError(failure, "enumerate the children of an element").WithDetails(details);
    }

    private static string AxisName(EnumerationAxis axis) =>
        axis == EnumerationAxis.FirstChild ? "first_child" : "next_sibling";

    /// <summary>Records whether every native predecessor of this edge was retained. The edge is certain only when
    /// every predecessor before it was retained.</summary>
    private static bool RetainedEdgeCertainty(ref bool prefixCertain, bool retained)
    {
        var edgeCertain = prefixCertain;
        prefixCertain &= retained;
        return edgeCertain;
    }

    /// <summary>A deadline whose expiry is no later than the cap. Core's deadline has no capped form, so the
    /// clamp is applied here.</summary>
    private static Deadline CappedBy(Deadline deadline, int timeoutMs) =>
        deadline with
        {
            ExpiresAt = Math.Min(deadline.ExpiresAt, Deadline.ExpiryFor(timeoutMs)),
            Capped = true,
        };

    private static byte SaturatingAdd(byte value, int delta) => (byte)Math.Min(byte.MaxValue, value + delta);

    private static void DisposeAll(List<TNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.Dispose();
        }
    }
}

/// <summary>Trims the suppressed-fault count onto the last reported error. A consumer that sees eight errors and no
/// count would read a systemic failure as a local one; the number travels, the dropped errors do not.</summary>
internal static class SuppressedFailureAnnotation
{
    internal static void Annotate(AdapterError error, uint suppressed)
    {
        if (suppressed == 0)
        {
            return;
        }

        var node = error.Details is { } text ? JsonNode.Parse(text) : null;
        if (node is null)
        {
            error.WithDetails(new JsonObject { ["suppressed_failures"] = (long)suppressed });
            return;
        }

        if (node is JsonObject map)
        {
            map["suppressed_failures"] = (long)suppressed;
            error.WithDetails(map);
        }
    }
}
