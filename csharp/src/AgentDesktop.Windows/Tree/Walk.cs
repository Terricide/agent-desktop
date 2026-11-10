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
        var kids = new List<TNode>();
        if (_source.Children(node, boundaryCandidate, kids) < 0)
        {
            _stats.Reads.NativeReadFailures += 1;
            _truncated = true;
            DisposeAll(kids);
            return NodeBuilder.Build(properties, label, children, null, true);
        }

        var truncatedByRawDepth = rawDepth >= _budget.MaxRawDepth && kids.Count > 0;
        var atBoundary = truncatedByRawDepth || childLogicalDepth > _budget.MaxLogicalDepth;
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

            uint? boundaryCount = kids.Count > 0 ? (uint)kids.Count : null;
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

    private static void DisposeAll(List<TNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.Dispose();
        }
    }
}