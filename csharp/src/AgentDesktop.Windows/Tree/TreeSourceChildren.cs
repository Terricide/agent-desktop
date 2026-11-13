namespace AgentDesktop.Windows.Tree;

/// <summary>Collects a node's children by walking the sibling list, for callers that want the whole list at once
/// rather than one step at a time. The raw-view walker is the surface this walks, so a node whose children are
/// content-view only - a glyph carried by a `statictext` under a button - gives up the same list here that the
/// snapshot walk sees. A list that ends at exhaustion is a complete answer and the fault is null; a walker fault
/// is the failure the caller classifies, and the children read before it stay in the list.</summary>
internal static class TreeSourceChildren
{
    internal static (List<TNode> Children, AutomationClassify.Failure? Fault) Collect<TNode>(
        TreeSource<TNode> source,
        TNode node,
        List<TNode> into)
        where TNode : class, IDisposable
    {
        var (first, firstFailure) = source.FirstChild(node);
        if (first is null)
        {
            return (into, firstFailure is { } failure && !failure.IsExhaustion ? failure : null);
        }

        var current = first;
        while (true)
        {
            var (sibling, siblingFailure) = source.NextSibling(current);
            into.Add(current);
            if (sibling is not null)
            {
                current = sibling;
                continue;
            }

            return (into, siblingFailure is null || siblingFailure.Value.IsExhaustion
                ? null
                : siblingFailure.Value);
        }
    }
}
