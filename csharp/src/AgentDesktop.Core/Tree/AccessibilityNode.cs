namespace AgentDesktop.Core.Tree;

/// <summary>A node in an accessibility tree, with the ref it was given when its role qualified for one.</summary>
public sealed record AccessibilityNode
{
    public string? RefId { get; init; }
    public string Role { get; init; } = "";
    public NodeIdentity Identity { get; init; } = new();
    public NodePresentation Presentation { get; init; } = new();
    public uint? ChildrenCount { get; init; }

    /// <summary>Set when this node's descendants were cut short. On a budget exhaustion it is the only honest marker, because the child-count read is unaffordable.</summary>
    public bool SubtreeTruncated { get; init; }

    public List<AccessibilityNode> Children { get; init; } = new();
}
