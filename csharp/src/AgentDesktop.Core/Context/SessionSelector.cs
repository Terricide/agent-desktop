namespace AgentDesktop.Core.Context;

/// <summary>Where the session id came from.</summary>
public enum SessionSource
{
    CommandLine,
    Environment,
    None,
}

/// <summary>The session a command runs against.</summary>
public sealed record SessionSelection(string? SessionId, SessionSource Source);

/// <summary>Session precedence: the --session flag, then AGENT_DESKTOP_SESSION, then none.
/// `session start` does not activate a session for later processes.</summary>
public static class SessionSelector
{
    public const string EnvironmentVariable = "AGENT_DESKTOP_SESSION";

    public static SessionSelection Select(string? flag, Func<string?> readEnvironment)
    {
        if (!string.IsNullOrWhiteSpace(flag))
        {
            return new SessionSelection(flag, SessionSource.CommandLine);
        }
        var fromEnvironment = readEnvironment();
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return new SessionSelection(fromEnvironment, SessionSource.Environment);
        }
        return new SessionSelection(null, SessionSource.None);
    }
}
