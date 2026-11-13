using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Locator;

/// <summary>The bounds and available actions a ref's capture recorded, each slot marked observed, absent, or unread.</summary>
public sealed record LocatorRefEvidence
{
    public LocatorField<Rect> WindowBounds { get; init; } = LocatorField<Rect>.Absent();
    public LocatorField<Rect> Bounds { get; init; } = LocatorField<Rect>.Absent();
    public LocatorField<List<string>> AvailableActions { get; init; } = LocatorField<List<string>>.Absent();
    public NodeDescriptor Descriptors { get; init; } = new();
}
