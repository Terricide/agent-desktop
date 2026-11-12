using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Resolution is the contract the agent depends on: a ref captured from a complete
/// window observation must resolve against the live window. A census that silently drops
/// children makes every ref unprovable, so the census has to be complete on a real window
/// before any ref can resolve. This runs against a live window because no stub can show that
/// the UIA child walk is exhaustive.</summary>
[SupportedOSPlatform("windows")]
public sealed class LiveWindowResolutionTests
{
    private static readonly TreeOptions FullWindow = new(
        50, true, false, false, SnapshotSurface.Window, false, false)
    {
        Skeleton = false,
    };

    private static (SnapshotCommand Snapshots, WindowsAdapter Adapter, WindowInfo Target)
    LiveExplorer()
    {
        var adapter = new WindowsAdapter();
        var state = new StateRoot(Path.Combine(Path.GetTempPath(),
            "ad-live-resolve-" + Guid.NewGuid().ToString("N")));
        var windows = adapter.ListWindows(new WindowFilter { App = "explorer.exe" },
            Deadline.After(15000));
        Assert.NotEmpty(windows);
        return (new SnapshotCommand(adapter, new SnapshotStore(state), new SessionStore(state)),
            adapter, windows[0]);
    }

    [Fact]
    public void AWholeWindowCensusOfALiveWindowIsComplete()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var (snapshots, _, target) = LiveExplorer();
        var captured = snapshots.CaptureWindow(target, null, FullWindow, 50000,
            Deadline.After(15000));

        Assert.True(captured.Budget.IsComplete,
            $"whole-window census ran out of budget at {captured.Budget.NodesObserved} nodes");
        Assert.True(captured.Complete, "whole-window census reported a truncated tree");
    }

    [Fact]
    public void EveryRefOfALiveWindowCensusResolvesStrictly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var (snapshots, adapter, target) = LiveExplorer();
        var captured = snapshots.CaptureWindow(target, null, FullWindow, 50000,
            Deadline.After(15000));
        Assert.True(captured.Budget.IsComplete, "census was truncated, so refs cannot be proven");

        var zeroMatch = new List<string>();
        var ambiguous = 0;
        var resolved = 0;
        var index = 0;
        foreach (var entry in captured.Map.Entries)
        {
            index++;
            try
            {
                using var handle = adapter.ResolveElementStrict(entry, Deadline.After(15000));
                Assert.False(handle.IsNull(), $"e{index} resolved to an empty handle");
                resolved++;
            }
            catch (AdapterError error) when (error.Code == ErrorCode.AmbiguousTarget)
            {
                ambiguous++;
            }
            catch (AdapterError error) when (error.Code == ErrorCode.ElementNotFound)
            {
                zeroMatch.Add("e" + index);
            }
        }

        Assert.NotEqual(0, resolved);
        Assert.NotEqual(0, ambiguous);
        Assert.Empty(zeroMatch);
    }
}
