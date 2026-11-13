using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Input;

/// <summary>Fresh pointer geometry and visibility from one observation.</summary>
public sealed record PointerObservation
{
    public Rect? Bounds { get; init; }
    public bool? Visible { get; init; }
    public bool Complete { get; init; }
}
