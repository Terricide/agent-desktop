namespace AgentDesktop.Cli;

/// <summary>The version the binary reports. It is the workspace version, so the C# port and
/// the Rust CLI answer with the same string.</summary>
public static class BuildInfo
{
    public const string Version = "1.2.0";
}
