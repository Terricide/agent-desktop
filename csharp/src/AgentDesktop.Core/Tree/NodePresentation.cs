namespace AgentDesktop.Core.Tree;

/// <summary>The presentation fields a node carries: hint, states, actions, bounds, descriptors.
/// The window-relative rectangle a ref stores is identity evidence, not output.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial record NodePresentation
{
    public string? Hint { get; init; }

    public List<string> States { get; init; } = new();

    public List<string> AvailableActions { get; init; } = new();

    public Rect? Bounds { get; init; }

    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.Skip)]
    public Rect? RefBounds { get; init; }

    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.Flatten)]
    public NodeDescriptor Descriptors { get; init; } = new();
}
