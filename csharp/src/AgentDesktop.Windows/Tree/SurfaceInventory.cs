using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree;

/// <summary>The surfaces the named process presents right now.
///
/// Every agent-facing top-level window is a `window` surface, the foreground one is also
/// `focused`, a window whose `WindowIsModal` reads true is a `sheet`, and a process with an open
/// menu presents one `menu` surface. The window-backed surfaces carry the same `w-&lt;hwnd&gt;`
/// identity `list_windows` emits, so a surface id is consumed by the window observation path
/// without a second lookup.
///
/// The classifications are the surface path's own - the window's `WindowIsModal` property and the
/// menu-state detector - so this inventory and `snapshot --surface` cannot disagree about what a
/// window or a menu is. A `WindowIsModal` read that failed is reported, not read as false. Shell
/// surfaces are not in a per-process inventory: they belong to the shell, not to any named
/// process. A process with no windows answers an empty list, which is a successful answer.
/// </summary>
internal static class SurfaceInventory
{
    /// <summary>The pid's agent-facing top-level windows, observed once and classified per window,
    /// then the process's open menu folded in under the menu-probe rule.</summary>
    internal static List<SurfaceInfo> ListSurfacesForProcess(ProcessIdentity process, Deadline deadline)
    {
        EnsureRemaining(deadline);
        RequireLiveProcess(process);
        var windows = WindowsOf(process, deadline);
        var observed = new List<ObservedWindow>(windows.Count);
        foreach (var window in windows)
        {
            observed.Add(ObserveWindow(window, deadline));
        }

        EnsureRemaining(deadline);
        var (surfaces, menu) = InventoryWithMenu(observed, process, () => LocateMenu(process, deadline));
        if (menu is not null)
        {
            surfaces.Add(MenuSurface(menu));
        }

        return surfaces;
    }

    /// <summary>One window as this inventory observed it. The sheet probe is carried as a fault
    /// rather than resolved here, so the rule about what a failed probe costs is decided in one
    /// pure place. Exactly one of <see cref="Sheet"/> and <see cref="SheetFault"/> is set.</summary>
    internal sealed record ObservedWindow(
        IntPtr Handle,
        string? Title,
        bool Foreground,
        bool? Sheet,
        AdapterError? SheetFault);

    /// <summary>Folds the menu probe's outcome into the window surfaces already collected.
    /// A menu probe that faults says nothing about the windows, which were read from the window
    /// census rather than from UIA, so it costs the process its `menu` classification and nothing
    /// else: each `window` entry names `menu` as unclassified. Propagating it would discard every
    /// window surface already collected.
    /// A budget exhaustion and a process that has died are not that: neither is evidence that no
    /// menu is open, and answering "no menu" for a probe that never finished would relocate the
    /// defect this fold exists to prevent. Those two codes propagate unchanged.</summary>
    internal static (List<SurfaceInfo> Surfaces, T? Menu) InventoryWithMenu<T>(
        List<ObservedWindow> observed,
        ProcessIdentity process,
        Func<T?> locate)
        where T : class
    {
        var surfaces = SurfacesOf(observed);
        try
        {
            return (surfaces, locate());
        }
        catch (AdapterError error) when (!RefusesTheListing(error))
        {
            TraceUnreadableMenu(process.Pid, error);
            return (MarkMenuUnread(surfaces), null);
        }
    }

    /// <summary>One window whose UIA root cannot be read costs that window its `sheet`
    /// classification and nothing else. Its `window` entry names `sheet` as unclassified, so the
    /// missing entry does not read as "no dialog open" - and a modal dialog is the likeliest reason
    /// a window stops answering UIA.</summary>
    internal static List<SurfaceInfo> SurfacesOf(List<ObservedWindow> observed)
    {
        var surfaces = new List<SurfaceInfo>();
        foreach (var window in observed)
        {
            var entry = Surface(SnapshotSurface.Window, window.Handle, window.Title);
            if (window.SheetFault is not null)
            {
                entry.Unclassified.Add(SnapshotSurface.Sheet.AsString());
            }

            surfaces.Add(entry);
            if (window.Foreground)
            {
                surfaces.Add(Surface(SnapshotSurface.Focused, window.Handle, window.Title));
            }

            if (window.SheetFault is not null)
            {
                TraceUnreadableWindow(window.Handle, window.SheetFault);
            }
            else if (window.Sheet == true)
            {
                surfaces.Add(Surface(SnapshotSurface.Sheet, window.Handle, window.Title));
            }
        }

        return surfaces;
    }

    /// <summary>The modal classification is the surface path's own predicate, read from the
    /// window's UIA root exactly as `snapshot --surface sheet` reads it. A provider that answers,
    /// or does not implement `WindowIsModal`, classifies the window; a read that fails is no answer.</summary>
    internal static bool IsModalSheet(IntPtr handle, Deadline deadline)
    {
        throw AdapterError.NotSupported("list-surfaces");
    }

    private static void EnsureRemaining(Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }
    }

    /// <summary>The caller's generation token must still be the process at its pid right now, the
    /// same fail-closed corroboration every other named-process entry point applies before answering.</summary>
    private static void RequireLiveProcess(ProcessIdentity process)
    {
        throw AdapterError.NotSupported("list-surfaces");
    }

    /// <summary>The pid's agent-facing top-level windows, from the one enumeration pass every window
    /// inventory on this platform shares, filtered by the same census predicate.</summary>
    private static List<IntPtr> WindowsOf(ProcessIdentity process, Deadline deadline)
    {
        var windows = new List<IntPtr>();
        var seen = 0;
        Exception? failure = null;
        WindowEnum.EnumerateTopLevel(window =>
        {
            if (seen > 0 && deadline.IsExpired())
            {
                failure = WindowRoot.DeadlineExceeded(deadline);
                return false;
            }

            seen++;
            if (WindowEnum.PassesFilter(window) && WindowEnum.LiveWindowOwner(window.Handle) == process.Pid.Value)
            {
                windows.Add(window.Handle);
            }

            return true;
        });
        if (failure is not null)
        {
            throw failure;
        }

        return windows;
    }

    private static ObservedWindow ObserveWindow(IntPtr handle, Deadline deadline)
    {
        bool? sheet = null;
        AdapterError? fault = null;
        try
        {
            sheet = IsModalSheet(handle, deadline);
        }
        catch (AdapterError error)
        {
            fault = error;
        }

        var title = WindowEnum.LiveWindowTitle(handle, (uint)WindowRoot.PumpProbeCapMs);
        return new ObservedWindow(
            handle,
            title.Length == 0 ? null : title,
            WindowEnum.IsForegroundWindow(handle),
            sheet,
            fault);
    }

    private static SurfaceInfo Surface(SnapshotSurface kind, IntPtr handle, string? title) => new()
    {
        Id = WindowEnum.WindowId(handle),
        Kind = kind.AsString(),
        Title = title,
    };

    private static List<SurfaceInfo> MarkMenuUnread(List<SurfaceInfo> surfaces)
    {
        var window = SnapshotSurface.Window.AsString();
        foreach (var entry in surfaces.Where(entry => entry.Kind == window))
        {
            entry.Unclassified.Add(SnapshotSurface.Menu.AsString());
        }

        return surfaces;
    }

    private static bool RefusesTheListing(AdapterError error) =>
        error.Code is ErrorCode.Timeout or ErrorCode.AppNotFound;

    private static object? LocateMenu(ProcessIdentity process, Deadline deadline)
    {
        throw AdapterError.NotSupported("list-surfaces");
    }

    private static SurfaceInfo MenuSurface(object menu)
    {
        throw AdapterError.NotSupported("list-surfaces");
    }

    private static void TraceUnreadableMenu(ProcessId pid, AdapterError error) =>
        global::System.Diagnostics.Trace.WriteLine(
            $"a process's open-menu classification could not be read ({error.Code}, pid {pid.Value}); its window surfaces stand");

    private static void TraceUnreadableWindow(IntPtr handle, AdapterError error) =>
        global::System.Diagnostics.Trace.WriteLine(
            $"a window's modal classification could not be read ({error.Code}, handle {handle.ToInt64()}); its other surfaces stand");
}
