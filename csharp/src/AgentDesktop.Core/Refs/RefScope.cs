namespace AgentDesktop.Core.Refs;

/// <summary>The window a ref was captured from, whether that capture read the window whole, and the
/// child-index path that re-finds the element from its root.</summary>
public sealed record RefScope
{
    public string WindowId { get; init; } = "";
    public bool CompleteObservation { get; init; }

    /// <summary>The ref the path is relative to, or null when the path starts at the window root.</summary>
    public string? RootRef { get; init; }

    /// <summary>Whether the path is absolute from the window root, overriding <see cref="RootRef"/>.</summary>
    public bool PathIsAbsolute { get; init; }

    /// <summary>The child indexes that lead from the root to the element the ref names.</summary>
    public List<int> Path { get; init; } = new();
}
