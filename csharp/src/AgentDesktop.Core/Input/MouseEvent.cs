using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Input;

/// <summary>A pointer gesture to synthesize.</summary>
public sealed record MouseEvent
{
    public string Kind { get; init; } = "";
    public Point Point { get; init; } = new Point(0.0, 0.0);
    public string Button { get; init; } = "left";
    public int ClickCount { get; init; } = 1;
    public double DeltaX { get; init; }
    public double DeltaY { get; init; }
}
