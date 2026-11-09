using AgentDesktop.Core.Locator;

namespace AgentDesktop.Core.Tree;

/// <summary>The identity fields a node carries: name, value, description, and the provider
/// identifier. Each is a field that can be known, absent, or unread, because a name that was
/// never read is not the same as a name that is definitively empty: the first is an incomplete
/// capture proof and the second is a complete one. The retained object is the adapter's own
/// token and never reaches the output.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial record NodeIdentity
{
    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.Skip)]
    public string? RetainedObject { get; init; }

    public LocatorField<string> Name { get; init; } = LocatorField<string>.Absent();

    public LocatorField<string> Value { get; init; } = LocatorField<string>.Absent();

    public LocatorField<string> Description { get; init; } = LocatorField<string>.Absent();

    public ElementIdentifier? NativeId { get; init; }
}
