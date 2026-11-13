using AgentDesktop.Core.Locator;

namespace AgentDesktop.Core.Adapter;

/// <summary>The identity a live read proved: name, description, and the identifiers it reported.</summary>
public sealed record LiveIdentity
{
    public LocatorField<string> Name { get; init; } = LocatorField<string>.Absent();
    public LocatorField<string> Description { get; init; } = LocatorField<string>.Absent();
    public IdentifierEvidence Identifiers { get; init; } = IdentifierEvidence.Absent();
}
