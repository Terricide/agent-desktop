namespace AgentDesktop.Core.Refs;

/// <summary>The snapshot a ref came from, and the observed token the adapter can re-check against.</summary>
public sealed record RefSource
{
    public string SnapshotId { get; init; } = "";
    public string RefId { get; init; } = "";
    public string? ObservedToken { get; init; }
}
