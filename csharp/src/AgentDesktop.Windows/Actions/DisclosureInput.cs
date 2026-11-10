namespace AgentDesktop.Windows.Actions;

/// <summary>Injected disclosure inputs shared by the live path and the unit-test seam.</summary>
internal sealed record DisclosureInput(bool WantExpanded, ExpandKind? Current, bool PatternOk, bool InvokeOk);
