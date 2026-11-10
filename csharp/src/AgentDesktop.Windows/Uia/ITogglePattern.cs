namespace AgentDesktop.Windows.Uia;

/// <summary>ITogglePattern's write method, the one the toggle command drives, matching the
/// Rust `UITogglePattern` trait.</summary>
internal interface ITogglePattern
{
    void Toggle();
}
