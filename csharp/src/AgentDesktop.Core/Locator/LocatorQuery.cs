namespace AgentDesktop.Core.Locator;

/// <summary>A query a find runs against a live tree: the identity fields it names, the text
/// it requires, the states it requires, and whether a name, description, value, or text match
/// is exact. A field the query names must have been read on the candidate, so a name query
/// never matches an element whose name was never read.</summary>
public sealed record LocatorQuery
{
    public IdentityPredicate Identity { get; init; } = new();
    public string? HasText { get; init; }
    public bool Exact { get; init; }
    public List<StatePredicate> States { get; init; } = new();
}
