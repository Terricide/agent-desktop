using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Pins the renderer-activation gate: only a window-rooted, window-surface observation
/// of a Chromium shell may demand the settle. A drill-down into a shell-shaped subtree returns
/// its thin tree, because the settle for that window has already run and core's loop would
/// re-walk the same subtree indefinitely.</summary>
public sealed class ChromiumActivationTests
{
    private static ObservationRequest Request(SnapshotSurface surface) =>
        new(new TreeOptions(50, false, false, false, surface, false, false), Deadline.After(5_000));

    private static ObservationRequest RequestAtDepth(int maxDepth) =>
        new(new TreeOptions((byte)maxDepth, false, false, false, SnapshotSurface.Window, false, false), Deadline.After(5_000));

    private static WalkStats Stats(int nodesVisited, int maxLogicalDepth) =>
        new(new TraversalCounts { NodesVisited = nodesVisited, MaxLogicalDepth = (byte)maxLogicalDepth }, new ReadCounts());

    private static ObservationRoot.Window AWindowRoot() =>
        new(new WindowInfo { Id = "w-1", App = "app", Pid = ProcessId.From(1) });

    private static ObservationRoot.Element ElementRoot() =>
        new(NativeHandle.Null(), new RefEntry(), null);

    [Fact]
    public void AWindowRootedWindowSurfaceObservationIsEligibleForActivation()
    {
        Assert.True(Chromium.ActivationEligible(AWindowRoot(), Request(SnapshotSurface.Window)));
    }

    [Fact]
    public void AWindowRootOnAFocusedSurfaceIsNotEligibleForActivation()
    {
        Assert.False(Chromium.ActivationEligible(AWindowRoot(), Request(SnapshotSurface.Focused)));
    }

    /// <summary>The surface clause is satisfied here, so only the root clause can refuse.
    /// Testing only window roots would leave this clause deletable with the suite green.</summary>
    [Fact]
    public void AnElementRootedObservationIsNeverEligibleForActivation()
    {
        var request = Request(SnapshotSurface.Window);

        Assert.Equal(SnapshotSurface.Window, ElementRoot().Surface());
        Assert.False(Chromium.ActivationEligible(ElementRoot(), request));
    }

    [Fact]
    public void AnApplicationRootIsNeverEligibleForActivation()
    {
        var root = new ObservationRoot.Application(new AppInfo { Name = "app", Pid = ProcessId.From(1) }, SnapshotSurface.Window);

        Assert.False(Chromium.ActivationEligible(root, Request(SnapshotSurface.Window)));
    }

    /// <summary>The #117 lesson pinned: a depth-clamped walk of a Chromium root never claims
    /// the shell shape, so it never demands activation.</summary>
    [Fact]
    public void ADepthClampedWalkNeverLooksShellShaped()
    {
        Assert.False(Chromium.IsShellShaped(Stats(12, 3), RequestAtDepth(3)));
    }

    [Fact]
    public void AFullDepthShellIsShellShaped()
    {
        Assert.True(Chromium.IsShellShaped(Stats(12, 12), RequestAtDepth(50)));
    }

    [Fact]
    public void AFullDepthSettledTreeIsNotShellShaped()
    {
        Assert.False(Chromium.IsShellShaped(Stats(165, 20), RequestAtDepth(50)));
    }

    /// <summary>A window root validates to the window it carries, so the caller reads the
    /// window's own fields rather than the root wrapper.</summary>
    [Fact]
    public void AWindowRootValidatesToItsStoredWindowInfo()
    {
        var window = new WindowInfo { Id = "w-1", App = "app", Pid = ProcessId.From(1) };

        Assert.Same(window, WindowRoot.Validate(new ObservationRoot.Window(window), SnapshotSurface.Window));
    }

    [Fact]
    public void AnElementRootIsRefusedBeforeAnyUiAutomationCall()
    {
        var root = new ObservationRoot.Element(NativeHandle.Null(), new RefEntry(), null);

        Assert.Equal(ErrorCode.PlatformNotSupported, Assert.Throws<AdapterError>(() => WindowRoot.Validate(root, SnapshotSurface.Window)).Code);
    }

    [Fact]
    public void AProcessOwnerWindowIsRefusedAsAppHasNoWindow()
    {
        var root = new ObservationRoot.Window(new WindowInfo { Id = "", App = "app", Pid = ProcessId.From(1) });

        Assert.Equal(ErrorCode.WindowNotFound, Assert.Throws<AdapterError>(() => WindowRoot.Validate(root, SnapshotSurface.Window)).Code);
    }

    [Fact]
    public void AFocusedSurfaceIsRefusedAsUnavailable()
    {
        var root = new ObservationRoot.Window(new WindowInfo { Id = "w-1", App = "app", Pid = ProcessId.From(1) });

        Assert.Throws<AdapterError>(() => WindowRoot.Validate(root, SnapshotSurface.Focused));
    }
}
