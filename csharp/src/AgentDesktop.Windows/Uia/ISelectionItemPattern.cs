namespace AgentDesktop.Windows.Uia;

/// <summary>ISelectionItemPattern's write method, the one the select command drives,
/// matching the Rust `UISelectionItemPattern` trait.</summary>
internal interface ISelectionItemPattern
{
    void Select();
}
