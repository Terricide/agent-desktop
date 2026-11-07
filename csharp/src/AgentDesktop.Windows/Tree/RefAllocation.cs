using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.Tree;

/// <summary>Gives a ref to every node that qualifies under core's InteractiveRoles, in
/// depth-first order, in the bare form core's RefMap allocates ("e1", "e2", ...).</summary>
internal static class RefAllocation
{
    internal static AccessibilityNode Allocate(AccessibilityNode root)
    {
        var allocated = 0;
        return Assign(root, ref allocated);
    }

    private static AccessibilityNode Assign(AccessibilityNode node, ref int allocated)
    {
        var actions = new HashSet<string>(node.Presentation.AvailableActions);
        var refId = InteractiveRoles.QualifiesForRef(node.Role, actions) ? "e" + ++allocated : null;
        var children = new List<AccessibilityNode>(node.Children.Count);
        foreach (var child in node.Children)
        {
            children.Add(Assign(child, ref allocated));
        }

        return node with { RefId = refId, Children = children };
    }
}
