using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Signal;

/// <summary>The process and app constraints a signal wait names. Both fields are intersected when both are set.</summary>
public sealed record SignalFilter
{
    public string? App { get; init; }

    public ProcessIdentity? Process { get; init; }
}
