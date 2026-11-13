using AgentDesktop.Core.Systems;
using AgentDesktop.Windows;
using Xunit;

namespace AgentDesktop.Windows.Tests;

public sealed class ProcessStateRulesTests
{
    private const uint StillActive = 259;

    [Fact]
    public void CleanExitCodeIsExited() =>
        Assert.Equal(ProcessState.ExitedWith(0), ProcessStateRules.StateFromExitCode(0));

    [Fact]
    public void CrashShapedExitCodeIsCrashed() =>
        Assert.Equal(
            ProcessState.CrashedWith(unchecked((int)0xC000_0005)),
            ProcessStateRules.StateFromExitCode(0xC000_0005));

    [Fact]
    public void StillActiveCodeWithoutWaitGateWouldLookExited() =>
        Assert.Equal(ProcessState.ExitedWith((int)StillActive), ProcessStateRules.StateFromExitCode(StillActive));

    [Fact]
    public void SingleTransientHungProbeStaysRunning()
    {
        var calls = 0;
        var state = ProcessStateRules.ClassifyHang(() => ++calls == 1 ? HangProbeResult.Hung : HangProbeResult.Responsive);
        Assert.Equal(ProcessState.Running, state);
    }

    [Fact]
    public void TwoConsecutiveHungProbesClassifyUnresponsive() =>
        Assert.Equal(ProcessState.Unresponsive, ProcessStateRules.ClassifyHang(() => HangProbeResult.Hung));

    [Fact]
    public void ImmediatelyResponsiveProbeClassifiesRunningWithOneCall()
    {
        var calls = 0;
        var state = ProcessStateRules.ClassifyHang(() => { calls++; return HangProbeResult.Responsive; });
        Assert.Equal(ProcessState.Running, state);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void LabelsAreTheCompactLowercaseTags()
    {
        Assert.Equal("running", ProcessState.Running.Label());
        Assert.Equal("exited", ProcessState.ExitedWith(0).Label());
        Assert.Equal("crashed", ProcessState.CrashedWith(1).Label());
        Assert.Equal("unresponsive", ProcessState.Unresponsive.Label());
    }
}
