namespace AgentDesktop.Core.Systems;

/// <summary>A process plus the instance token that distinguishes one running copy from another.</summary>
public sealed record ProcessIdentity(ProcessId Pid, string Instance);
