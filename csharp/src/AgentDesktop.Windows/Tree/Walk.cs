using System.Text.Json.Nodes;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.Tree;

/// <summary>One depth-first walk over a tree source, mirroring TreeWalk in
/// crates/windows/src/tree/walk/walker_enumerate.rs. The deadline is checked on entering a
/// node and again after its read. A node past the depth budget keeps its child count when
/// the count read completed. A cut by the deadline, the sibling cap or the raw depth cap
/// marks the cut node and every ancestor subtree_truncated, and the walk incomplete. A
/// child list that fails to enumerate fails the whole walk with ACTION_FAILED rather than
/// passing a truncated tree off as complete. Every node the source hands out is disposed
/// exactly once, on every exit path.
///
/// There is no cycle guard: the Rust walk keys an ancestor path on runtime ids, which this
/// read set does not carry. A provider that reports an ancestor as its own descendant is
/// bounded by the raw depth cap and the deadline, and its walk reports incomplete.</summary>
internal sealed class Walk<TNode> where TNode : class, IDisposable
{
    internal const int MaxReportedFaults = 8;

    private readonly TreeSource<TNode> _source;

    private readonly WalkBudget _budget;

    private readonly JsonArray _faults = new();

    private int _suppressedFaults;

    private uint _nodesObserved;

    private bool _complete = true;

    internal Walk(TreeSource<TNode> source, WalkBudget budget)
    {
        _source = source;
        _budget = budget;
    }

    /// <summary>Walks the tree under the root, taking ownership of the root.</summary>
    internal ObservedTree Run(TNode root)
    {
        var tree = Visit(root, 0, 0);
        if (_faults.Count > 0)
        {
            throw FaultError();
        }

        if (tree is null)
        {
            throw AdapterError.Timeout("Element tree walk ended before reading its root");
        }

        return new ObservedTree(RefAllocation.Allocate(tree), _complete, !_complete, _nodesObserved);
    }

    private AccessibilityNode? Visit(TNode node, byte logicalDepth, byte rawDepth)
    {
        using (node)
        {
            if (_budget.Deadline.IsExpired())
            {
                _complete = false;
                return null;
            }

            return VisitEntered(node, logicalDepth, rawDepth);
        }
    }

    private AccessibilityNode? VisitEntered(TNode node, byte logicalDepth, byte rawDepth)
    {
        var properties = _source.Read(node);
        var childLogicalDepth = _source.IsWebWrapper(properties)
            ? logicalDepth
            : (byte)Math.Min(logicalDepth + 1, byte.MaxValue);
        if (_budget.Deadline.IsExpired())
        {
            _complete = false;
            return null;
        }

        var countOnly = rawDepth >= _budget.MaxRawDepth || childLogicalDepth > _budget.MaxLogicalDepth;
        var children = new List<TNode>();
        var readComplete = ReadChildren(node, rawDepth, countOnly, children);
        var truncatedByRawDepth = rawDepth >= _budget.MaxRawDepth && children.Count > 0;
        if (truncatedByRawDepth || childLogicalDepth > _budget.MaxLogicalDepth)
        {
            uint? count = readComplete && children.Count > 0 ? (uint)children.Count : null;
            DisposeFrom(children, 0);
            return Finish(properties, new List<AccessibilityNode>(), count, readComplete && !truncatedByRawDepth);
        }

        var (visited, childrenComplete) = VisitChildren(children, childLogicalDepth, rawDepth);
        return Finish(properties, visited, null, readComplete && childrenComplete);
    }

    private bool ReadChildren(TNode node, byte rawDepth, bool countOnly, List<TNode> into)
    {
        var complete = true;
        var hr = _source.Children(node, countOnly, into);
        if (hr < 0)
        {
            RecordFault(hr, rawDepth, into.Count);
            complete = false;
        }

        if (into.Count > _budget.MaxSiblings)
        {
            DisposeFrom(into, _budget.MaxSiblings);
            complete = false;
        }

        return complete;
    }

    /// <summary>A child the deadline dropped leaves this subtree incomplete: the parent is
    /// a cut node even though its own child list was read in full.</summary>
    private (List<AccessibilityNode> Children, bool Complete) VisitChildren(List<TNode> nodes, byte logicalDepth, byte rawDepth)
    {
        var visited = new List<AccessibilityNode>(nodes.Count);
        var complete = true;
        var next = 0;
        try
        {
            while (next < nodes.Count)
            {
                var subtree = Visit(nodes[next++], logicalDepth, (byte)(rawDepth + 1));
                if (subtree is null)
                {
                    complete = false;
                    continue;
                }

                visited.Add(subtree);
                complete &= !subtree.SubtreeTruncated;
            }
        }
        finally
        {
            DisposeFrom(nodes, next);
        }

        return (visited, complete);
    }

    private AccessibilityNode Finish(NodeProperties properties, List<AccessibilityNode> children, uint? childrenCount, bool subtreeComplete)
    {
        if (!subtreeComplete)
        {
            _complete = false;
        }

        _nodesObserved++;
        return NodeBuilder.Build(properties, children, childrenCount, !subtreeComplete);
    }

    /// <summary>Shape only: the HRESULT, the depth, and how many children were retrieved
    /// first. No name, class or value enters an error, because error text reaches traces.</summary>
    private void RecordFault(int hresult, byte rawDepth, int childIndex)
    {
        if (_faults.Count >= MaxReportedFaults)
        {
            _suppressedFaults++;
            return;
        }

        _faults.Add((JsonNode)new JsonObject
        {
            ["kind"] = "child_enumeration_failed",
            ["axis"] = "children",
            ["raw_depth"] = rawDepth,
            ["child_index"] = childIndex,
            ["hresult"] = $"0x{hresult:X8}",
        });
    }

    private AdapterError FaultError()
    {
        if (_suppressedFaults > 0 && _faults[^1] is JsonObject last)
        {
            last["suppressed_failures"] = _suppressedFaults;
        }

        return new AdapterError(ErrorCode.ActionFailed, "Element tree walk encountered an enumeration failure")
            .WithDetails(new JsonObject { ["faults"] = _faults });
    }

    private static void DisposeFrom(List<TNode> nodes, int start)
    {
        for (var index = start; index < nodes.Count; index++)
        {
            nodes[index].Dispose();
        }

        if (start < nodes.Count)
        {
            nodes.RemoveRange(start, nodes.Count - start);
        }
    }
}
