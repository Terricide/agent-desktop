namespace AgentDesktop.Core.Refs;

/// <summary>One ref's capture record. A ref resolves only when one complete read of its window proved it unique at capture and exactly one live element matches its role and stable fields now.</summary>
public sealed record RefEntry
{
    public RefProcess Process { get; init; } = new();
    public RefEntryIdentity Identity { get; init; } = new();
    public RefGeometry Geometry { get; init; } = new();
    public RefCapabilities Capabilities { get; init; } = new();
    public RefSource Source { get; init; } = new();
    public RefScope Scope { get; init; } = new();
    public string? UniquenessReason { get; init; }
}
