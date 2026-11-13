namespace AgentDesktop.Core.Systems;

/// <summary>Window focus, accessibility, and the geometry state a read proved.</summary>
public sealed record WindowState
{
    public bool IsFocused { get; init; }

    public bool Accessible { get; init; } = true;

    public bool? Minimized { get; init; }

    public bool? Visible { get; init; }
}
