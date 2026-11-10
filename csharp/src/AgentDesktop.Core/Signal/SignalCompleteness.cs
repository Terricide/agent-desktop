namespace AgentDesktop.Core.Signal;

/// <summary>Whether each signal pass ran to completion. A pass that was not requested reports false.</summary>
public readonly record struct SignalCompleteness(bool Windows, bool Apps, bool Surfaces)
{
    public static SignalCompleteness Complete { get; } = new(true, true, true);
}
