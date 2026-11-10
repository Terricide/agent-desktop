namespace AgentDesktop.Windows.Uia;

/// <summary>IExpandCollapsePattern's write methods, the ones the disclosure command drives,
/// matching the Rust `UIExpandCollapsePattern` trait.</summary>
internal interface IExpandCollapsePattern
{
    void Expand();

    void Collapse();
}
