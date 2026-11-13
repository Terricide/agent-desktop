namespace AgentDesktop.Core.Systems;

/// <summary>Which windows to list. The app name is enforced by the adapter, which may resolve it against display names, bundle identifiers, or pids.</summary>
public sealed record WindowFilter
{
    public bool FocusedOnly { get; init; }
    public string? App { get; init; }
}
