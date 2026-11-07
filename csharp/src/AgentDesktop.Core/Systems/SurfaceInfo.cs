namespace AgentDesktop.Core.Systems;

/// <summary>A surface a window presents. Unclassified names the kinds that could not be read, so a missing entry is not read as an observed absence.</summary>
public sealed record SurfaceInfo
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? Title { get; init; }
    public int? ItemCount { get; init; }
    public List<string> Unclassified { get; init; } = new();
}
