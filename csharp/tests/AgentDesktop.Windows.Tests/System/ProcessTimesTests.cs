using System.Diagnostics;
using AgentDesktop.Windows;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Verifies the process-generation token: the shape it takes, the generations it
/// separates, and the exit evidence the process object gives. The checks run against the test
/// process itself, a real process the runner owns, so no process is invented.</summary>
public sealed class ProcessTimesTests
{
    private static uint Self => (uint)Process.GetCurrentProcess().Id;

    [Fact]
    public void TheCurrentProcessHasAGenerationToken()
    {
        var token = ProcessTimes.TokenForPid(Self);

        Assert.NotNull(token);
        Assert.StartsWith("windows-proc-v1:", token);
        Assert.Equal(3, token!.Split(':').Length);
    }

    [Fact]
    public void AStoredTokenStillMatchesTheProcessItWasCapturedFrom()
    {
        Assert.True(ProcessTimes.MatchesInstance(Self, ProcessTimes.TokenForPid(Self)!));
    }

    [Fact]
    public void ATokenFromADifferentGenerationFailsClosed()
    {
        var token = ProcessTimes.TokenForPid(Self)!;
        var seconds = long.Parse(token.Split(':')[1]);

        Assert.False(ProcessTimes.MatchesInstance(Self, $"windows-proc-v1:{seconds + 1}:0"));
    }

    [Fact]
    public void AnUnrecognisedTokenMatchesNothing()
    {
        Assert.False(ProcessTimes.MatchesInstance(Self, "macos-proc-v1:1:2"));
        Assert.False(ProcessTimes.MatchesInstance(Self, "windows-proc-v1:not-a-number:2"));
        Assert.False(ProcessTimes.MatchesInstance(Self, "windows-proc-v1:1"));
        Assert.Null(ProcessTimes.ParseToken("windows-proc-v1:1:2:3"));
    }

    [Fact]
    public void TwoTokensInTheSameSecondAreDistinctWhenTheSubSecondTickDiffers()
    {
        var token = ProcessTimes.TokenForPid(Self)!;
        var parts = token.Split(':');
        var otherTick = $"windows-proc-v1:{parts[1]}:{long.Parse(parts[2]) + 1}";

        Assert.NotEqual(token, otherTick);
        Assert.False(ProcessTimes.MatchesInstance(Self, otherTick));
    }

    [Fact]
    public void TheCurrentProcessIsRunning()
    {
        Assert.True(ProcessTimes.ProcessIsRunning(Self));
    }

    [Fact]
    public void AProcessThatExitedIsReportedExited()
    {
        var child = Process.Start("cmd.exe", "/c exit 0");
        Assert.True(child.WaitForExit(10000));
        var pid = (uint)child.Id;

        Assert.True(ProcessTimes.WaitForExit(pid, TimeSpan.FromSeconds(1)));
        Assert.False(ProcessTimes.ProcessIsRunning(pid));
    }

    [Fact]
    public void AProcessThatDoesNotExitInsideTheWindowIsNotReportedExited()
    {
        var child = Process.Start("cmd.exe", "/c ping -n 5 127.0.0.1 > nul");

        Assert.False(ProcessTimes.WaitForExit((uint)child.Id, TimeSpan.FromMilliseconds(200)));

        child.Kill();
    }

    [Fact]
    public void AProcessNumberThatNamesNothingIsReportedExited()
    {
        Assert.True(ProcessTimes.WaitForExit(uint.MaxValue, TimeSpan.FromMilliseconds(10)));
    }
}
