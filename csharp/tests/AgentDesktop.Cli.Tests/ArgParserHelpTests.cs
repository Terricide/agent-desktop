using AgentDesktop.Cli;
using Xunit;

namespace AgentDesktop.Cli.Tests;

/// <summary>`agent-desktop --help` and a bare `agent-desktop` are the first things a caller types, and
/// both must print the command list rather than an argument error. The Rust CLI prints the list with
/// exit 0, so an INVALID_ARGS answer here sends a stranger down a path the build cannot take.
///
/// The help is built from the command table at runtime, so the test checks that the list the help
/// prints is exactly the names the CLI accepts: help cannot promise a command the CLI refuses.</summary>
public class ArgParserHelpTests
{
    [Fact]
    public void HelpAtArgvZeroAsksForTheCommandList()
    {
        var outcome = ArgParser.Parse(["--help"]);

        Assert.True(outcome.Help);
        Assert.Null(outcome.Command);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void ShortHelpFlagAsksForTheCommandList()
    {
        var outcome = ArgParser.Parse(["-h"]);

        Assert.True(outcome.Help);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public void BareInvocationAsksForTheCommandList()
    {
        var outcome = ArgParser.Parse([]);

        Assert.True(outcome.Help);
    }

    /// <summary>A name outside the table is an argument error, and the help path must not swallow it.</summary>
    [Fact]
    public void ANameOutsideTheCommandTableIsRefusedWithoutTheHelpPath()
    {
        var outcome = ArgParser.Parse(["frobnicate"]);

        Assert.False(outcome.Help);
        Assert.Null(outcome.Command);
        Assert.Contains("frobnicate", outcome.Error);
    }

    /// <summary>The command list the help prints, read from the printed text.</summary>
    private static List<string> NamesInHelp()
    {
        var lines = HelpText.Write("agent-desktop.exe").Split('\n').ToList();
        var start = lines.IndexOf("Commands:") + 1;
        return lines.Skip(start).TakeWhile(line => line.StartsWith("  ")).Select(l => l.Trim()).ToList();
    }

    [Fact]
    public void HelpPrintsExactlyTheCommandsTheTableAccepts()
    {
        Assert.Equal(CommandTable.NamesInOrder, NamesInHelp());
    }

    /// <summary>The help names the commands that fail closed on this platform, so the list the help
    /// prints cannot be read as a promise that the held-input commands work.</summary>
    [Fact]
    public void HelpNamesTheCommandsThatFailClosedOnWindows()
    {
        var text = HelpText.Write("agent-desktop.exe");

        Assert.Contains("key-down, key-up, mouse-down, mouse-up", text);
        Assert.Contains("skills get windows", text);
    }
}
