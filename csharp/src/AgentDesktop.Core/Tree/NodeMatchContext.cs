namespace AgentDesktop.Core.Tree;

/// <summary>The observable fields a find query compares against, read from a node. A field
/// that was never read is null, so a query naming it never matches. The children carry the
/// text search: a text query matches a node when the node's own name, description, or value
/// contains it, or any descendant's does.</summary>
public sealed record NodeMatchContext
{
    public string Role { get; init; } = "";
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? NativeId { get; init; }
    public string? Value { get; init; }
    public List<string> States { get; init; } = new();
    public List<AccessibilityNode> Children { get; init; } = new();

    public static NodeMatchContext From(AccessibilityNode node) => new()
    {
        Role = node.Role,
        Name = node.Identity.Name.Value,
        Description = node.Identity.Description.Value,
        NativeId = node.Identity.NativeId?.Value,
        Value = node.Identity.Value.Value,
        States = node.Presentation.States,
        Children = node.Children,
    };
}
