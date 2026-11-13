using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Locator;

/// <summary>An app inventory request. Exact lookups cannot authorize mutation until the inventory is complete.</summary>
public sealed record AppInventoryQuery
{
    public AppInventoryQueryKind Kind { get; init; }
    public string? Name { get; init; }
    public string? BundleId { get; init; }

    public static AppInventoryQuery All() => new() { Kind = AppInventoryQueryKind.All };

    public static AppInventoryQuery Exact(string name, string? bundleId) =>
        new() { Kind = AppInventoryQueryKind.Exact, Name = name, BundleId = bundleId };

    public static AppInventoryQuery NameOrAlias(string name) =>
        new() { Kind = AppInventoryQueryKind.NameOrAlias, Name = name };

    public bool MatchesDisplayName(string displayName) =>
        Name is null || AppInfo.AppNameMatches(displayName, Name);
}
