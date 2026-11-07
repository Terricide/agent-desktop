namespace AgentDesktop.Core.Refs;

/// <summary>The window a ref was captured from, and whether that capture read the window whole.</summary>
public sealed record RefScope
{
    public string WindowId { get; init; } = "";
    public bool CompleteObservation { get; init; }
}
