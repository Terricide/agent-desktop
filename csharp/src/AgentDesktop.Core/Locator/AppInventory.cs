using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Locator;

/// <summary>The applications a platform found, and the records whose evidence was incomplete within the query.</summary>
public sealed record AppInventory
{
    public List<AppInfo> Apps { get; init; } = new();
    public List<string> Skipped { get; init; } = new();
}
