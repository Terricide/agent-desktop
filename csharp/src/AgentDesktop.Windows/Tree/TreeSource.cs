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

    /// <summary>Appends the node's children and returns the HRESULT. A failure keeps the
    /// children appended before it. A count-only read is for a boundary node, whose children
    /// are counted and released but never read.</summary>
    int Children(TNode node, bool countOnly, List<TNode> into);

    bool IsWebWrapper(ElementProperties properties);
}
