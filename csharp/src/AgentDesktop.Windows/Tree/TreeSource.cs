namespace AgentDesktop.Windows.Tree;

/// <summary>What a walk reads from: the live UI Automation tree, or a purpose-built stub in
/// tests. Every node the source hands out belongs to the walk, which disposes it exactly
/// once. Mirrors the TreeSource trait in crates/windows/src/tree/walk/walker.rs.</summary>
internal interface TreeSource<TNode> where TNode : class, IDisposable
{
    NodeProperties Read(TNode node);

    /// <summary>Appends the node's children and returns the HRESULT. A failure keeps the
    /// children appended before it. A count-only read is for a boundary node, whose children
    /// are counted and released but never read.</summary>
    int Children(TNode node, bool countOnly, List<TNode> into);

    bool IsWebWrapper(NodeProperties properties);
}
