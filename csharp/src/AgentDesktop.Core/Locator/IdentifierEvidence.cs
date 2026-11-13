using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Locator;

/// <summary>The identifiers a live read reported, or the fact that it reported none.</summary>
public sealed record IdentifierEvidence
{
    public bool Present { get; init; }
    public List<ElementIdentifier> Identifiers { get; init; } = new();

    public static IdentifierEvidence Absent() => new() { Present = false };
}
