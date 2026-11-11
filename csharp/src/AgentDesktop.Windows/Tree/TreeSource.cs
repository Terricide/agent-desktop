namespace AgentDesktop.Windows.Tree;

/// <summary>What a walk reads from: the live UI Automation tree, or a purpose-built stub in
/// tests. Every node the source hands out belongs to the walk, which disposes it exactly
/// once. Mirrors the TreeSource trait in crates/windows/src/tree/walk/walker.rs.</summary>
internal interface TreeSource<TNode> where TNode : class, IDisposable
{
    ElementProperties Read(TNode node);

    /// <summary>What reading this node's `LabeledBy` relation produced. The relation is read
    /// from the node's cache when the source batches, and from the provider otherwise. The target's
    /// own name and `IsPassword` are always read live, because a cached element-returning property
    /// hands back a target whose cached reads fail for the properties the same request asked for (A15-3).</summary>
    LabelOutcome ReadLabel(TNode node);

    /// <summary>Descends one level. A null node with an exhaustion failure means the node has no
    /// children; a null node with any other failure means the child could not be read, which the
    /// walk must not read as an empty subtree. Mirrors `first_child`.</summary>
    (TNode? Node, AutomationClassify.Failure? Failure) FirstChild(TNode node);

    /// <summary>Advances along a sibling list. A null node with an exhaustion failure ends the
    /// list; any other failure is a genuine fault. Mirrors `next_sibling`.</summary>
    (TNode? Node, AutomationClassify.Failure? Failure) NextSibling(TNode node);

    bool IsWebWrapper(ElementProperties properties);
}
