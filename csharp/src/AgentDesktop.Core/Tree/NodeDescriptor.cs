namespace AgentDesktop.Core.Tree;

/// <summary>The optional presentation descriptors: role refinement, provider control type, placeholder, DOM classes. Every field is absent by default so a node that supplies none serializes as it did before this group existed.</summary>
public sealed record NodeDescriptor
{
    public string? Subrole { get; init; }
    public string? RoleDescription { get; init; }
    public string? Placeholder { get; init; }
    public List<string> DomClasses { get; init; } = new();
}
