namespace AgentDesktop.Core.Tree;

/// <summary>The identity fields a node carries: name, value, description, and the provider identifier.</summary>
public sealed record NodeIdentity
{
    public string? RetainedObject { get; init; }
    public string? Name { get; init; }
    public string? Value { get; init; }
    public string? Description { get; init; }
    public ElementIdentifier? NativeId { get; init; }
}
