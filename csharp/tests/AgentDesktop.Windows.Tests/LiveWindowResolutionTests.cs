using System.Diagnostics;
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

    [Fact(Skip = "requires a live UIA tree")]
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

    [Fact(Skip = "requires a live UIA tree")]
    public void EveryRefOfALiveWindowCensusResolvesToItsCaptureProof()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var target = OpenDenseExplorerFolder();
        var adapter = new WindowsAdapter();
        var state = new StateRoot(Path.Combine(Path.GetTempPath(),
            "ad-live-resolve-" + Guid.NewGuid().ToString("N")));
        var snapshots = new SnapshotCommand(adapter, new SnapshotStore(state), new SessionStore(state));
        var captured = snapshots.CaptureWindow(target, null, FullWindow, 50000,
            Deadline.After(15000));

        Assert.True(captured.Budget.IsComplete, "census was truncated, so refs cannot be proven");
        Assert.NotEmpty(captured.Map.Entries);

        var uniqueResolved = 0;
        var uniqueRefused = new List<string>();
        var notUniqueRefused = 0;
        var index = 0;
        foreach (var entry in captured.Map.Entries)
        {
            index++;
            var name = "e" + index;
            try
            {
                using var handle = adapter.ResolveElementStrict(entry, Deadline.After(15000));
                Assert.False(handle.IsNull(), $"{name} resolved to an empty handle");
                Assert.True(entry.Identity.UniqueAtCapture,
                    $"{name} resolved although capture did not prove a unique match");
                uniqueResolved++;
            }
            catch (AdapterError error) when (error.Code == ErrorCode.AmbiguousTarget)
            {
                Assert.False(entry.Identity.UniqueAtCapture,
                    $"{name} is ambiguous although capture proved a unique match");
                notUniqueRefused++;
            }
            catch (AdapterError error) when (error.Code == ErrorCode.ElementNotFound)
            {
                uniqueRefused.Add(name);
            }
        }

        Assert.Empty(uniqueRefused);
        Assert.NotEqual(0, uniqueResolved);
        Assert.NotEqual(0, notUniqueRefused);
    }

    /// <summary>Opens a File Explorer window over a folder of files, so the census is dense
    /// enough to contain both uniquely named items and repeated labels. A window Explorer has
    /// already opened for an earlier run is reused, because Explorer throttles new windows when
    /// several are open. The window is awaited with the adapter's own window listing, not a
    /// fixed sleep.</summary>
    private static WindowInfo OpenDenseExplorerFolder()
    {
        var adapter = new WindowsAdapter();
        var deadline = Deadline.After(30000);
        var marker = "ad-dense-";

        var existing = adapter.ListWindows(new WindowFilter { App = "explorer.exe" }, deadline)
            .FirstOrDefault(w => w.Title.Contains(marker));
        if (existing is not null)
        {
            return existing;
        }

        var folder = Path.Combine(Path.GetTempPath(), marker + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        for (var i = 0; i < 40; i++)
        {
            File.WriteAllText(Path.Combine(folder, $"file{i}.txt"), "x");
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });

        while (true)
        {
            var match = adapter.ListWindows(new WindowFilter { App = "explorer.exe" }, deadline)
                .FirstOrDefault(w => w.Title.Contains(marker));
            if (match is not null)
            {
                return match;
            }

            if (deadline.IsExpired())
            {
                throw new Exception("the File Explorer window for the dense folder never appeared");
            }
        }
    }
}
