namespace AgentDesktop.Core.Locator;

/// <summary>Everything one observation of one element recorded.</summary>
public sealed record LocatorEvidence
{
    public LocatorField<string> Role { get; init; } = LocatorField<string>.Absent();
    public LocatorField<string> Name { get; init; } = LocatorField<string>.Absent();
    public LocatorField<string> Description { get; init; } = LocatorField<string>.Absent();
    public LocatorField<string> Value { get; init; } = LocatorField<string>.Absent();
    public IdentifierEvidence Identifiers { get; init; } = IdentifierEvidence.Absent();
    public LocatorField<List<string>> States { get; init; } = LocatorField<List<string>>.Absent();
    public LocatorRefEvidence RefEvidence { get; init; } = new();
}
