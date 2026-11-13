namespace AgentDesktop.Core.Tree;

/// <summary>The optional presentation descriptors this schema adds: a finer role refinement, the
/// provider's control-type description, an input placeholder, and a web element's class list.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial record NodeDescriptor
{
    public string? Subrole { get; init; }

    public string? RoleDescription { get; init; }

    public string? Placeholder { get; init; }

    public List<string> DomClasses { get; init; } = new();
}
