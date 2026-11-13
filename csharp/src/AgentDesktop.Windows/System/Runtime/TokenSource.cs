namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// Which token a read consumes: this process's own token, which the read opens and closes itself, or a
/// handle a caller already opened and keeps owning. A zero handle is never a valid token, so it names the
/// current process.
/// </summary>
internal readonly record struct TokenSource(IntPtr Handle)
{
    /// <summary>This process's own token.</summary>
    internal static TokenSource CurrentProcess => default;

    /// <summary>Whether the read should open this process's own token.</summary>
    internal bool IsCurrentProcess => Handle == IntPtr.Zero;
}
