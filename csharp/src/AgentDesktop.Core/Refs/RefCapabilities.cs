namespace AgentDesktop.Core.Refs;

/// <summary>The capture proof: the states the element carried in the observation that captured it, the
/// actions it advertised, and whether one complete read proved it unique. The recorded states are what a
/// state property answers from when the live read finds nothing.</summary>
public sealed record RefCapabilities
{
    public List<string> States { get; init; } = new();
    public List<string> AvailableActions { get; init; } = new();
    public bool UniqueAtCapture { get; init; }
}
