namespace AgentDesktop.Core.Tree;

/// <summary>A node in an accessibility tree, with the ref it was given when its role qualified
/// for one. Its JSON writer is generated, so the fields appear in the order declared here.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial record AccessibilityNode
{
    public string? RefId { get; init; }

    public string Role { get; init; } = "";

    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.Flatten)]
    public NodeIdentity Identity { get; init; } = new();

    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.Flatten)]
    public NodePresentation Presentation { get; init; } = new();

    public uint? ChildrenCount { get; init; }

    /// <summary>Set when this node's descendants were cut short. On a budget exhaustion it is
    /// the only honest marker, because the child-count read is unaffordable.</summary>
    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.SkipWhenFalse)]
    public bool SubtreeTruncated { get; init; }

    public List<AccessibilityNode> Children { get; init; } = new();
}
