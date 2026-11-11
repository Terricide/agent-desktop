using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.Tree;

/// <summary>Walks a source subtree into the shared node shape inside the walk budget. Logical
/// depth, raw depth, siblings, and the deadline are all bounds the walk respects; a node whose
/// descendants were cut short carries the truncation marker, because the child-count read is
/// unaffordable once the budget is spent. Refs are allocated by core's qualification rule after
/// the tree is built, so the ref order is depth-first over the tree the caller sees.</summary>
internal sealed class Walk<TNode> where TNode : class, IDisposable
{
    private readonly TreeSource<TNode> _source;
    private readonly WalkBudget _budget;
    private readonly WalkStats _stats = new(new TraversalCounts(), new ReadCounts());
    private uint _observed;
    private bool _truncated;

    internal Walk(TreeSource<TNode> source, WalkBudget budget)
    {
        _source = source;
        _budget = budget;
    }

    /// <summary>Walks the subtree and returns the tree with the accounting that travels with
    /// it. The completeness the tree reports is the walk's own verdict, and the stats are what
    /// a caller needs to judge a thin tree: a depth-clamped walk reached a shallow
    /// <c>MaxLogicalDepth</c>, which is why it cannot be read as a genuinely thin tree.</summary>
    internal (ObservedTree Tree, WalkStats Stats) Run(TNode root)
    {
        var tree = RefAllocation.Allocate(Node(root, 0, 0));
        return (new ObservedTree(tree, !_truncated, _truncated, _observed), _stats);
    }

    private AccessibilityNode Node(TNode node, int logicalDepth, int rawDepth)
    {
        _observed += 1;
        _stats.Traversal.NodesVisited += 1;
        _stats.Reads.ObservationAttempts += 1;
        if (logicalDepth > _stats.Traversal.MaxLogicalDepth)
        {
            _stats.Traversal.MaxLogicalDepth = (byte)logicalDepth;
        }

        if (rawDepth > _stats.Traversal.MaxRawDepth)
        {
            _stats.Traversal.MaxRawDepth = (byte)rawDepth;
        }

        var properties = _source.Read(node);
        var label = _source.ReadLabel(node);
        var children = new List<AccessibilityNode>();
        var webWrapper = _source.IsWebWrapper(properties);
        if (webWrapper)
        {
            _stats.Traversal.WebWrapperNodes += 1;
        }

        var childLogicalDepth = webWrapper ? logicalDepth : logicalDepth + 1;
        var boundaryCandidate = rawDepth >= _budget.MaxRawDepth || childLogicalDepth > _budget.MaxLogicalDepth;
        var (kids, enumerationComplete) = ReadChildren(node);
        var truncatedByRawDepth = rawDepth >= _budget.MaxRawDepth && kids.Count > 0;
        var atBoundary = truncatedByRawDepth || childLogicalDepth > _budget.MaxLogicalDepth;
        if (!enumerationComplete && !atBoundary)
        {
            _truncated = true;
            DisposeAll(kids);
            return NodeBuilder.Build(properties, label, children, null, true);
        }

        if (atBoundary)
        {
            if (truncatedByRawDepth)
            {
                _stats.Traversal.ChildHits += 1;
                _truncated = true;
            }
            else
            {
                _stats.Traversal.DepthHits += 1;
            }

            uint? boundaryCount = enumerationComplete && kids.Count > 0 ? (uint)kids.Count : null;
            DisposeAll(kids);
            return NodeBuilder.Build(properties, label, children, boundaryCount, truncatedByRawDepth);
        }

        var sibling = 0;
        var cut = false;
        foreach (var kid in kids)
        {
            if (sibling >= _budget.MaxSiblings)
            {
                _truncated = true;
                cut = true;
                DisposeAll(kids[sibling..]);
                break;
            }

            children.Add(Node(kid, childLogicalDepth, rawDepth + 1));
            sibling++;
            kid.Dispose();
        }

        return NodeBuilder.Build(properties, label, children, null, cut);
    }

    /// <summary>Walks the sibling list one walker step at a time. A single path that retires end-of-siblings and
    /// a cross-process fault through the same arm turns a hung target into a truncated tree that reports complete,
    /// so every failure is classified: exhaustion ends the list and leaves the subtree complete, anything else
    /// marks it incomplete. A boundary node's children are counted by the same walk that a full node's are, so a
    /// count is the number of children the walk saw, not the number a control-view query returned.</summary>
    private (List<TNode> Kids, bool Complete) ReadChildren(TNode node)
    {
        var kids = new List<TNode>();
        var (first, firstFailure) = _source.FirstChild(node);
        if (first is null)
        {
            if (firstFailure is { } failure && !failure.IsExhaustion)
            {
                _stats.Reads.NativeReadFailures += 1;
                return (kids, false);
            }

            return (kids, true);
        }

        var current = first;
        while (true)
        {
            if (kids.Count >= _budget.MaxSiblings)
            {
                _stats.Traversal.ChildHits += 1;
                _truncated = true;
                current.Dispose();
                return (kids, false);
            }

            var (sibling, siblingFailure) = _source.NextSibling(current);
            kids.Add(current);
            if (sibling is not null)
            {
                current = sibling;
                continue;
            }

            if (siblingFailure is null || siblingFailure.Value.IsExhaustion)
            {
                break;
            }

            _stats.Reads.NativeReadFailures += 1;
            _truncated = true;
            return (kids, false);
        }

        return (kids, true);
    }

    private static void DisposeAll(List<TNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.Dispose();
        }
    }
}