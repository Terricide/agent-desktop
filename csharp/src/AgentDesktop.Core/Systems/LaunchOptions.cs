namespace AgentDesktop.Core.Systems;

/// <summary>Options accepted by a launch. <see cref="AttachIfRunning"/> defaults to true, which attaches to an
/// already-running instance instead of failing. Set it to false to require a fresh launch: the adapter then fails
/// with a structured error naming the running pid instead of attaching.</summary>
public sealed record LaunchOptions
{
    public IReadOnlyList<string> Args { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The working directory for the launched process, or null to inherit the caller's.</summary>
    public string? Cwd { get; init; }

    public ulong TimeoutMs { get; init; } = 5_000;

    public bool AttachIfRunning { get; init; } = true;

    /// <summary>Brings the application forward so it presents a window. A document-based application creates its first
    /// window in response to activation, so a caller that needs a window has to ask for one; waiting without asking
    /// waits for an event that never fires.</summary>
    public bool Activate { get; init; }

    /// <summary>The resolved, concrete DevTools protocol port. Core resolves a requested 0 to a real free port before the
    /// adapter sees it, so by the time a launch runs this is either null or one exact port.</summary>
    public ushort? CdpPort { get; init; }
}
