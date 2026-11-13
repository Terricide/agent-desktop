namespace AgentDesktop.Core.Storage;

/// <summary>All state lives under one root: `~/.agent-desktop` by default, relocated by
/// AGENT_DESKTOP_HOME. Nothing else writes outside it.</summary>
public sealed class StateRoot
{
    public const string EnvironmentVariable = "AGENT_DESKTOP_HOME";

    public const string RootDirectoryName = ".agent-desktop";

    public StateRoot(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public string Sessions => System.IO.Path.Combine(Path, "sessions");

    public string Snapshots => System.IO.Path.Combine(Path, "snapshots");

    /// <summary>The root a command uses: AGENT_DESKTOP_HOME, then the user's home.</summary>
    public static StateRoot FromEnvironment(Func<string?> readEnvironment)
    {
        var home = readEnvironment();
        return new StateRoot(string.IsNullOrWhiteSpace(home)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), RootDirectoryName)
            : home);
    }

    public string SessionDirectory(string sessionId) => System.IO.Path.Combine(Sessions, sessionId);

    public string SnapshotDirectory(string namespaceName, string snapshotId) =>
        System.IO.Path.Combine(Snapshots, namespaceName, snapshotId);
}
