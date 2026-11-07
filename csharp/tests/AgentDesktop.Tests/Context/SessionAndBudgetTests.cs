using AgentDesktop.Core.Context;
using AgentDesktop.Core.Snapshot;
using Xunit;

namespace AgentDesktop.Tests.Context;

public class SessionAndBudgetTests
{
    [Fact]
    public void TheFlagWinsOverTheEnvironment()
    {
        var selection = SessionSelector.Select("--session-id", () => "from-env");
        Assert.Equal("--session-id", selection.SessionId);
        Assert.Equal(SessionSource.CommandLine, selection.Source);
    }

    [Fact]
    public void TheEnvironmentIsUsedWhenNoFlagIsGiven()
    {
        var selection = SessionSelector.Select(null, () => "from-env");
        Assert.Equal("from-env", selection.SessionId);
        Assert.Equal(SessionSource.Environment, selection.Source);
    }

    [Fact]
    public void NoSessionIsSelectedWhenNeitherIsSet()
    {
        Assert.Equal(SessionSource.None, SessionSelector.Select(null, () => null).Source);
    }

    [Fact]
    public void RunningOutOfBudgetReportsAPartialSnapshotNotAFailure()
    {
        var budget = new SnapshotBudget(2);
        Assert.True(budget.TryObserve());
        Assert.True(budget.TryObserve());
        Assert.False(budget.TryObserve());
        Assert.False(budget.IsComplete);
        Assert.Equal(2, budget.NodesObserved);
    }
}
