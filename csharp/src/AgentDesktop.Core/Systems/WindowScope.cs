namespace AgentDesktop.Core.Systems;

/// <summary>Which window a command observes: by id, by title, the active window, the
/// frontmost window, or the window nearest a point. Nothing set means the app's own
/// window, chosen by focus and visibility.</summary>
public sealed record WindowScope
{
    public string? WindowId { get; init; }

    public string? Title { get; init; }

    public bool Active { get; init; }

    public bool Frontmost { get; init; }

    public double? NearX { get; init; }

    public double? NearY { get; init; }

    public bool IsEmpty =>
        WindowId is null && Title is null && !Active && !Frontmost && NearX is null && NearY is null;
}
