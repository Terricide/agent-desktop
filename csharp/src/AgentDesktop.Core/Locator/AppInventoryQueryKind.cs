namespace AgentDesktop.Core.Locator;

/// <summary>How an app inventory is asked for: everything, one exact identity, or a display name.</summary>
public enum AppInventoryQueryKind
{
    All,
    Exact,
    NameOrAlias,
}
