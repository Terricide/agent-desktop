namespace AgentDesktop.Core.Input;

/// <summary>A pointer drag from one point to another.</summary>
public sealed record DragParams
{
    public Point From { get; init; }
    public Point To { get; init; }
    public long DropDelayMs { get; init; }
    public string Type { get; init; } = "";
}
