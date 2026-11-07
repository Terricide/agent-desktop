namespace AgentDesktop.Core.Actions;

/// <summary>One mechanism an adapter tried, and how it ended.</summary>
public sealed record ActionStep(string Label, string Outcome, string Mechanism);
