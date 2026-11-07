namespace AgentDesktop.Core.Storage;

/// <summary>Sessions are directories under the state root. `session start` creates one; it does
/// not activate a session for later processes, which must pass the id.</summary>
public sealed class SessionStore
{
    public SessionStore(StateRoot root)
    {
        _root = root;
    }

    private readonly StateRoot _root;

    public bool Exists(string sessionId) => Directory.Exists(_root.SessionDirectory(sessionId));

    public string Start(string sessionId)
    {
        var directory = _root.SessionDirectory(sessionId);
        Directory.CreateDirectory(directory);
        return sessionId;
    }

    /// <summary>The namespace snapshots live in: the session's own, or the shared one when no
    /// session is selected. Snapshot lookup never searches another session's namespace.</summary>
    public string Namespace(string? sessionId) => sessionId ?? "default";
}
