using System.Runtime.CompilerServices;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>
/// Every command the CLI table names must have an arm in the CLI dispatch. A command in the table with no
/// arm answers PLATFORM_NOT_SUPPORTED, and the CLI reports that for a command the platform does implement,
/// which is the misleading direction: the caller is told to use a build that advertises the capability,
/// when the capability is there and only the arm is missing.
///
/// Both files are read as source, because the test project targets net8.0 and the CLI targets
/// net8.0-windows, so neither can be called from here.
/// </summary>
public class CommandSurfaceTests
{
    /// <summary>The commands the CLI table names that have no dispatch arm yet. Each one answers
    /// PLATFORM_NOT_SUPPORTED. The list is the recorded gap and can only shrink: a command added to the table
    /// without an arm fails the gate, and a command wired up must be removed from the list.
    ///
    /// The window, capture, notification, session, trace and system commands are here because the adapter
    /// methods they need are not ported, not because the arm is missing.</summary>
    private static readonly string[] Unwired =
    [
        "screenshot", "hover", "launch", "close-app",
        "open-system-surface", "list-surfaces", "list-notifications",
        "dismiss-notification", "dismiss-all-notifications", "notification-action",
        "wait", "status", "batch", "skills", "session",
        "cursor-overlay", "trace",
    ];

    [Fact]
    public void EveryCommandInTheTableHasAnArmOrIsInTheRecordedGap()
    {
        string dispatch = File.ReadAllText(DispatchSourcePath());
        List<string> unwired = new();

        foreach (string command in TableNames())
        {
            if (!dispatch.Contains(ArmText(command)) && !Unwired.Contains(command))
            {
                unwired.Add(command);
            }
        }

        Assert.Empty(unwired);
    }

    /// <summary>A command wired into the dispatch must not stay in the recorded gap, so the list shrinks as
    /// the surface lands and never grows to hide a new command.</summary>
    [Fact]
    public void TheRecordedGapNamesOnlyCommandsThatHaveNoArm()
    {
        string dispatch = File.ReadAllText(DispatchSourcePath());
        List<string> stale = Unwired.Where(name => dispatch.Contains(ArmText(name))).ToList();

        Assert.Empty(stale);
    }

    /// <summary>The command names the CLI accepts. A name that is not in the table is not a command, so the
    /// table is the surface the dispatch must cover.</summary>
    [Fact]
    public void TheTableNamesTheCommandsTheCliAccepts()
    {
        List<string> names = TableNames();
        Assert.Contains("mouse-click", names);
        Assert.Contains("clipboard-set", names);
        Assert.Contains("double-click", names);
    }

    /// <summary>The dispatch arm for a command: the quoted name followed by the arrow the switch uses.</summary>
    private static string ArmText(string command) => "\"" + command + "\" =>";

    /// <summary>The quoted names in the command table's list.</summary>
    private static List<string> TableNames() =>
        global::System.Text.RegularExpressions.Regex.Matches(
            File.ReadAllText(CommandTableSourcePath()), "\"([a-z-]+)\"")
            .Select(m => m.Groups[1].Value)
            .Where(n => !n.Contains("--"))
            .ToList();

    private static string DispatchSourcePath() =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(CallerPath())!,
            "..", "..", "src", "AgentDesktop.Cli", "Dispatch.cs"));

    private static string CommandTableSourcePath() =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(CallerPath())!,
            "..", "..", "src", "AgentDesktop.Cli", "CommandTable.cs"));

    private static string CallerPath([CallerFilePath] string path = "") => path;
}
