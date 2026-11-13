using AgentDesktop.Core.Snapshot;

namespace AgentDesktop.Core.Tree;

/// <summary>What an observation should read: depth, bounds, interactivity, compactness, surface, skeleton.</summary>
public readonly record struct TreeOptions(
    byte MaxDepth,
    bool IncludeBounds,
    bool InteractiveOnly,
    bool Compact,
    SnapshotSurface Surface,
    bool Skeleton,
    bool ForceRendererAccessibility)
{
    public static TreeOptions Default() => new(10, false, false, false, SnapshotSurface.Window, false, false);

    public TreeOptions WithRefIdentityBounds() => this with { IncludeBounds = true };
}
