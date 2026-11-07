namespace AgentDesktop.Core.Tree;

/// <summary>The presentation fields a node carries: hint, states, actions, bounds, descriptors.</summary>
public sealed record NodePresentation
{
    public string? Hint { get; init; }
    public List<string> States { get; init; } = new();
    public List<string> AvailableActions { get; init; } = new();
    public Rect? Bounds { get; init; }
    public Rect? RefBounds { get; init; }
    public NodeDescriptor Descriptors { get; init; } = new();
}
