namespace AgentDesktop.Core.Tree;

/// <summary>The identity fields a node carries: name, value, description, and the provider
/// identifier. The retained object is the adapter's own token and never reaches the output.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial record NodeIdentity
{
    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.Skip)]
    public string? RetainedObject { get; init; }

    public string? Name { get; init; }

    public string? Value { get; init; }

    public string? Description { get; init; }

    public ElementIdentifier? NativeId { get; init; }
}
