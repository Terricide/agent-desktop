using AgentDesktop.Core.Snapshot;

namespace AgentDesktop.Core.Refs;

/// <summary>The snapshot a ref came from, and the observed token the adapter can re-check against.</summary>
public sealed record RefSource
{
    public string SnapshotId { get; init; } = "";
    public string RefId { get; init; } = "";
    public string? ObservedToken { get; init; }

    /// <summary>The window the ref was captured from, when the capture had one.</summary>
    public string? SourceWindowId { get; init; }

    /// <summary>The title of the window the ref was captured from.</summary>
    public string? SourceWindowTitle { get; init; }

    /// <summary>The application the ref was captured from.</summary>
    public string? SourceApp { get; init; }

    /// <summary>The surface the ref was captured from.</summary>
    public SnapshotSurface Surface { get; init; }
}
