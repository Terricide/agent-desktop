namespace AgentDesktop.Core.Adapter;

/// <summary>Whether the OS reports a permission as granted, denied, not required, or unprobed. Unprobed is not the same fact as a user denying one.</summary>
public enum PermissionState
{
    Unknown,
    Granted,
    Denied,
    NotRequired,
}
