using AgentDesktop.Core.Tree;
using AgentDesktop.Windows;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>The census filter is the four criteria the Windows census measured over 147
/// top-level windows: visible, non-zero rect, not cloaked by the shell, not a tool window.
/// Each criterion is tested in both directions, so a filter that silently drops one of them
/// fails here rather than in a user's inventory.</summary>
public sealed class WindowCensusTests
{
    private static EnumeratedWindow Window(
        bool visible = true,
        bool iconic = false,
        bool cloaked = false,
        bool tool = false,
        double width = 800,
        double height = 600) =>
        new(new IntPtr(0x1000), visible, iconic, cloaked, tool, new Rect(0, 0, width, height));

    [Fact]
    public void AWindowTheAgentMeansPassesTheFilter()
    {
        Assert.True(WindowEnum.PassesFilter(Window()));
    }

    [Fact]
    public void AnInvisibleWindowIsNotInTheInventory()
    {
        Assert.False(WindowEnum.PassesFilter(Window(visible: false)));
    }

    [Fact]
    public void AZeroWidthWindowIsNotInTheInventory()
    {
        Assert.False(WindowEnum.PassesFilter(Window(width: 0)));
    }

    [Fact]
    public void AZeroHeightWindowIsNotInTheInventory()
    {
        Assert.False(WindowEnum.PassesFilter(Window(height: 0)));
    }

    [Fact]
    public void ACloakedWindowIsNotInTheInventory()
    {
        Assert.False(WindowEnum.PassesFilter(Window(cloaked: true)));
    }

    [Fact]
    public void AToolWindowIsNotInTheInventory()
    {
        Assert.False(WindowEnum.PassesFilter(Window(tool: true)));
    }

    [Fact]
    public void AMinimizedWindowStillCountsAsAWindow()
    {
        Assert.True(WindowEnum.PassesFilter(Window(iconic: true)));
    }

    [Fact]
    public void TheWindowIdRoundTripsToTheHandleTheObservationPathResolves()
    {
        var handle = new IntPtr(0x1234);
        Assert.Equal(handle, WindowEnum.HandleFor(WindowEnum.WindowId(handle)));
    }

    [Fact]
    public void AnIdentifierThatNamesNoHandleResolvesToNothing()
    {
        Assert.Equal(IntPtr.Zero, WindowEnum.HandleFor("not-a-window"));
        Assert.Equal(IntPtr.Zero, WindowEnum.HandleFor("w-not-a-number"));
    }
}
