using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Refs;

/// <summary>The process a ref was captured from.</summary>
public sealed record RefProcess { public string Name { get; init; } = ""; public ProcessId Pid { get; init; } public string? Instance { get; init; } }
