using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Input;

/// <summary>A pointer drag from one point to another.</summary>
public sealed record DragParams
{
    public Point From { get; init; } = new Point(0.0, 0.0);
    public Point To { get; init; } = new Point(0.0, 0.0);
    public long DropDelayMs { get; init; }
    public string Type { get; init; } = "";
}
