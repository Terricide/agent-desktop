using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Refs;

/// <summary>The geometry a ref's capture recorded, used for hit-testing, never for identity.</summary>
public sealed record RefGeometry
{
    public Rect? Bounds { get; init; }
    public Rect? WindowBounds { get; init; }
}
