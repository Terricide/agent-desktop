using AgentDesktop.Core.Snapshot;

namespace AgentDesktop.Core.Locator;

/// <summary>Where a find looks: the app, the window, the subtree root, and the surface.</summary>
public sealed record FindScopeArgs
{
    public string? App { get; init; }
    public string? WindowId { get; init; }
    public string? Root { get; init; }
    public SnapshotSurface Surface { get; init; } = SnapshotSurface.Window;
}

/// <summary>What a find matches on: the identity fields, the text, and whether a match is exact.</summary>
public sealed record FindFilterArgs
{
    public string? Role { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? NativeId { get; init; }
    public string? Value { get; init; }
    public string? Text { get; init; }
    public bool Exact { get; init; }
}

/// <summary>Which of the matches to return. At most one mode is accepted, so a request that
/// asks for two is refused before the application is read.</summary>
public sealed record FindSelectionArgs
{
    public bool Count { get; init; }
    public bool First { get; init; }
    public bool Last { get; init; }
    public int? Nth { get; init; }
    public int? Limit { get; init; }
}

/// <summary>A find: where to look, what to match, which matches to return, and the budget.</summary>
public sealed record FindArgs
{
    public FindScopeArgs Scope { get; init; } = new();
    public FindFilterArgs Filter { get; init; } = new();
    public List<string> StateFlags { get; init; } = new();
    public FindSelectionArgs Selection { get; init; } = new();
    public long? TimeoutMs { get; init; }
}
