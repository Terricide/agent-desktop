namespace AgentDesktop.Core.Locator;

/// <summary>How a candidate element is identified: its role, name, description, native
/// identifier, and value. A field the query names must be present on the candidate, so a
/// name query never matches an element whose name was never read.</summary>
public sealed record IdentityPredicate
{
    public string? Role { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? NativeId { get; init; }
    public string? Value { get; init; }
}
