namespace AgentDesktop.Core.Refs;

/// <summary>The capture proof: the actions the element advertised, and whether one complete read proved it unique.</summary>
public sealed record RefCapabilities
{
    public List<string> AvailableActions { get; init; } = new();
    public bool UniqueAtCapture { get; init; }
}
