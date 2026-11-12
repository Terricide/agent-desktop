using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Refs;

/// <summary>
/// One whole-window observation, counted once into the field subsets its entries record, so a
/// boundary offered as a skeleton anchor is proven by the same rule resolution applies later,
/// rather than by a fresh scan per entry. Port of <c>CaptureProof</c> in
/// <c>crates/core/src/refs/ref_capture_proof.rs</c>.
///
/// The count is the comparison resolution runs: the predicate the node itself forms, measured
/// against the evidence every observed node reported. A predicate two nodes match is stored with
/// the last of them, so it never equals the node an entry was built from. The Windows adapter
/// reports one native identifier per node, so a node with no identifier is a complete read that
/// recorded none, not an unread list; the incomplete-list arm of the Rust proof has no
/// counterpart here.
/// </summary>
internal sealed class CaptureCensus
{
    private readonly AccessibilityNode _root;
    private readonly bool _wholeWindow;
    private readonly List<AccessibilityNode> _nodes = [];
    private readonly Dictionary<AccessibilityNode, int> _indices = new();
    private readonly List<(int Bits, AccessibilityNode Node)> _unread = [];
    private readonly Dictionary<int, Dictionary<FullPredicate, int>> _buckets = new();

    /// <summary>Counts the observation. Only a complete whole-window read proves anything: a
    /// truncated read never saw the whole window, so a field it did not see cannot be called
    /// unique.</summary>
    internal CaptureCensus(AccessibilityNode root, bool wholeWindow)
    {
        _root = root;
        _wholeWindow = wholeWindow;
        Flatten(root);
        for (var index = 0; index < _nodes.Count; index++)
        {
            var bits = UnreadBits(_nodes[index]);
            if (bits != 0)
            {
                _unread.Add((bits, _nodes[index]));
            }
        }
    }

    /// <summary>Whether the node at the allocation path is proven unique. The path is read the way
    /// allocation wrote it: child positions in the presented tree, which skips no child the
    /// observation kept. Port of <c>proves</c> in
    /// <c>crates/core/src/refs/ref_capture_proof.rs</c>.
    ///
    /// The presented tree is a cut of the counted observation, so a node the projection rebuilt
    /// is not the node the census holds; the path is what ties them.</summary>
    internal bool ProvesAt(List<int> path)
    {
        var node = _root;
        foreach (var step in path)
        {
            if (step < 0 || step >= node.Children.Count)
            {
                return false;
            }
            node = node.Children[step];
        }
        return Proves(node);
    }

    /// <summary>Unread nodes with their path, for a diagnostic dump.</summary>
    internal IEnumerable<(string Path, int Bits)> UnreadNodes()
    {
        List<(string Path, int Bits)> found = [];
        void Walk(AccessibilityNode node, string path)
        {
            var bits = _unread.FirstOrDefault(b => b.Node == node).Bits;
            if (bits != 0)
            {
                found.Add((path, bits));
            }

            foreach (var (i, child) in node.Children.Select((c, i) => (i, c)))
            {
                Walk(child, path.Length == 0 ? i.ToString() : path + "." + i);
            }
        }

        Walk(_root, string.Empty);
        return found;
    }

    /// <summary>Nodes that could not answer at least one identity field, by bit. Diagnostic only.</summary>
    internal string UnreadSummary() => string.Join(", ", new[]
    {
        "role=" + _unread.Count(b => (b.Bits & FullPredicate.RoleBit) != 0),
        "name=" + _unread.Count(b => (b.Bits & FullPredicate.NameBit) != 0),
        "desc=" + _unread.Count(b => (b.Bits & FullPredicate.DescriptionBit) != 0),
    });

    /// <summary>How many nodes report this predicate for its own field subset. Diagnostic only.</summary>
    internal int CountFor(FullPredicate predicate)
    {
        var total = 0;
        foreach (var node in _nodes)
        {
            foreach (var key in KeysOf(node, predicate.Mask()))
            {
                if (key == predicate)
                {
                    total++;
                }
            }
        }

        return total;
    }

    /// <summary>Whether the node is proven unique over this observation: exactly one node matches
    /// its predicate, no node leaves a recorded field unread that this predicate cannot rule out,
    /// and the read covered the whole window.</summary>
    internal bool Proves(AccessibilityNode node)
    {
        if (!_wholeWindow)
        {
            return false;
        }

        var predicate = FullPredicate.OfNode(node);
        var mask = predicate.Mask();
        if (!_indices.TryGetValue(node, out var index)
            || !Bucket(mask).TryGetValue(predicate, out var winner)
            || winner != index)
        {
            return false;
        }

        foreach (var (bits, observed) in _unread)
        {
            if ((bits & (mask | FullPredicate.RoleBit)) != 0
                && predicate.Compare(observed) == IdentityMatch.Unknown)
            {
                return false;
            }
        }

        return true;
    }

    private void Flatten(AccessibilityNode node)
    {
        _indices[node] = _nodes.Count;
        _nodes.Add(node);
        foreach (var child in node.Children)
        {
            Flatten(child);
        }
    }

    /// <summary>The nodes each predicate of this field subset matched, kept only where exactly one
    /// node matched. The subset with no fields is counted: a node whose role alone is unique in the
    /// window is proven by that, which is the bucket Rust indexes at 0.</summary>
    private Dictionary<FullPredicate, int> Bucket(int mask)
    {
        if (_buckets.TryGetValue(mask, out var done))
        {
            return done;
        }

        var counted = new Dictionary<FullPredicate, (int Count, int Index)>();
        for (var index = 0; index < _nodes.Count; index++)
        {
            foreach (var key in KeysOf(_nodes[index], mask))
            {
                counted[key] = counted.TryGetValue(key, out var seen)
                    ? (seen.Count + 1, index)
                    : (1, index);
            }
        }

        var unique = new Dictionary<FullPredicate, int>();
        foreach (var (key, (count, index)) in counted)
        {
            if (count == 1)
            {
                unique[key] = index;
            }
        }

        _buckets[mask] = unique;
        return unique;
    }

    /// <summary>The keys this node reports for a field subset. A node that cannot answer a
    /// recorded field reports no key for that subset, so it cannot be counted as a match.</summary>
    private static List<FullPredicate> KeysOf(AccessibilityNode node, int mask)
    {
        if (string.IsNullOrWhiteSpace(node.Role))
        {
            return [];
        }

        var role = node.Role;
        string? name = null;
        if ((mask & FullPredicate.NameBit) != 0)
        {
            name = FullPredicate.StableName(
                role,
                Known(node.Identity.Name),
                Known(node.Identity.Value));
            if (name is null)
            {
                return [];
            }
        }

        string? description = null;
        if ((mask & FullPredicate.DescriptionBit) != 0)
        {
            description = NonBlank(Known(node.Identity.Description));
            if (description is null)
            {
                return [];
            }
        }

        if ((mask & FullPredicate.NativeIdBit) == 0)
        {
            return [new FullPredicate(role, null, name, description)];
        }

        var identifier = NonBlank(node.Identity.NativeId?.Value);
        return identifier is null
            ? []
            : [new FullPredicate(role, identifier, name, description)];
    }

    /// <summary>The fields this node could not answer, so a predicate that records one of them
    /// reaches unknown here exactly as it will at resolution.</summary>
    private static int UnreadBits(AccessibilityNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Role))
        {
            return FullPredicate.RoleBit;
        }

        var bits = 0;
        if (FullPredicate.StableNameField(
                node.Role,
                node.Identity.Name,
                node.Identity.Value).IsUnknown())
        {
            bits |= FullPredicate.NameBit;
        }

        if (node.Identity.Description.IsUnknown())
        {
            bits |= FullPredicate.DescriptionBit;
        }

        return bits;
    }

    private static string? Known(LocatorField<string> field) =>
        field.IsKnown() ? field.Value : null;

    private static string? NonBlank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text;
}
