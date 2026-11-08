namespace AgentDesktop.Core.Input;

/// <summary>A modifier held with a key. The names are the shared contract the CLI accepts,
/// so `cmd` and `meta` name the same key and a platform maps each to its own virtual key.</summary>
public enum Modifier
{
    Meta,
    Ctrl,
    Alt,
    Shift,
}
