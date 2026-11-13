namespace AgentDesktop.Cli;

/// <summary>The text `agent-desktop --help` prints, and the text a bare `agent-desktop` prints.
/// Only names this build accepts are listed, so the help cannot promise a command the CLI refuses.
/// Per-command flags are not printed here, so the text points at the skill for what differs on
/// Windows and names the commands this build does not implement.</summary>
public static class HelpText
{
    public static string Write(string binaryName)
    {
        var lines = new List<string>
        {
            "See and operate desktop apps on Windows through the accessibility tree",
            "",
            $"Usage: {binaryName} <command> [flags]",
            "",
            "Commands:",
        };

        foreach (var name in CommandTable.NamesInOrder)
        {
            lines.Add($"  {name}");
        }

        lines.Add("");
        lines.Add("Every command prints one JSON envelope; see docs/json-output.md for the contract.");
        lines.Add("Windows behaviour and its limits are in skills/agent-desktop-windows/SKILL.md.");
        lines.Add("This build does not serve the skill from the binary, so `skills get windows` fails closed.");
        lines.Add("The four held-input commands (key-down, key-up, mouse-down, mouse-up) fail closed on Windows.");

        return string.Join("\n", lines);
    }
}
