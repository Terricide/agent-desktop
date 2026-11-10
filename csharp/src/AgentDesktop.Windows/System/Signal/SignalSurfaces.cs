using global::System;
using global::System.Collections.Generic;
using global::System.Linq;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Signal;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Menu;
using AgentDesktop.Windows.System.Window;

namespace AgentDesktop.Windows.System.Signal;

/// <summary>
/// The surface signals for every process named in an app population, scoped to exactly those processes.
/// </summary>
/// <remarks>
/// Core rejects <c>surface-appeared</c> and <c>surface-dismissed</c> without <c>--app</c>, so this is only ever called
/// with a filtered, non-empty app population and never performs a desktop-wide walk. Each app contributes at most one
/// Menu signal, because the menu predicate is a per-process boolean, and zero or more Sheet signals, one per modal
/// owned window. An app whose process instance is absent is skipped rather than given a fabricated instance;
/// <c>signal_inventory</c> already excludes such entries, so this is a defensive backstop.
/// </remarks>
internal static class SignalSurfaces
{
    /// <summary>
    /// The surface kinds the baseline can emit. The two constructors in this file are the only places a surface signal
    /// is built, so this list is what "a signal emitted implies the surface advertised" is asserted against.
    /// </summary>
    internal static readonly SnapshotSurface[] SignalSurfaceKinds = { SnapshotSurface.Menu, SnapshotSurface.Sheet };

    internal static List<SurfaceSignal> AppScopedSurfaces(IReadOnlyList<AppInfo> apps, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var targets = IdentifiedTargets(apps);
        if (targets.Count == 0)
        {
            return new List<SurfaceSignal>();
        }

        var surfaces = SheetSurfaces(targets, deadline);
        var pids = targets.Values.Select(target => target.Pid).ToList();
        var menusOpen = MenuStateMulti.MenusOpenFor(pids, deadline);
        foreach (var target in targets.Values)
        {
            if (menusOpen.TryGetValue(target.Pid, out var open) && open)
            {
                surfaces.Add(MenuSignal(target));
            }
        }

        return surfaces;
    }

    private static Dictionary<ProcessId, SurfaceTarget> IdentifiedTargets(IReadOnlyList<AppInfo> apps)
    {
        var targets = new Dictionary<ProcessId, SurfaceTarget>();
        foreach (var app in apps)
        {
            if (app.ProcessInstance is not { } instance)
            {
                continue;
            }

            targets[app.Pid] = new SurfaceTarget(app.Pid, app.Name, instance);
        }

        return targets;
    }

    private static SurfaceSignal MenuSignal(SurfaceTarget target) =>
        new(SnapshotSurface.Menu, target.Name, target.Pid, target.Instance, $"menu-{target.Pid.Value}", null);

    private static SurfaceSignal SheetSignal(SurfaceTarget target, IntPtr handle) =>
        new(SnapshotSurface.Sheet, target.Name, target.Pid, target.Instance, WindowEnum.WindowId(handle), null);

    /// <summary>
    /// One top-level scan shared across every target pid, so a filter naming several processes still costs a single
    /// enumeration rather than one per process. A candidate window is admitted the way the ordinary window inventory
    /// admits one, plus carrying an owner: the owned-window half of the measured modal signature.
    /// </summary>
    private static List<SurfaceSignal> SheetSurfaces(Dictionary<ProcessId, SurfaceTarget> targets, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var candidates = new List<IntPtr>();
        try
        {
            WindowEnum.EnumerateTopLevel(window =>
            {
                if (WindowEnum.PassesFilter(window) && HasOwner(window.Handle))
                {
                    candidates.Add(window.Handle);
                }

                return true;
            });
        }
        catch (AdapterError error)
        {
            throw ListingRetry.NarrowToPermittedCodes(error);
        }

        var surfaces = new List<SurfaceSignal>();
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index > 0)
            {
                deadline.EnsureRemaining();
            }

            var handle = candidates[index];
            if (WindowEnum.LiveWindowOwner(handle) is not { } ownerPid)
            {
                continue;
            }

            if (!targets.TryGetValue(ProcessId.From(ownerPid), out var target))
            {
                continue;
            }

            try
            {
                if (IsModalSheet(handle, deadline))
                {
                    surfaces.Add(SheetSignal(target, handle));
                }
            }
            catch (AdapterError error) when (error.Code == ErrorCode.WindowNotFound)
            {
                continue;
            }
            catch (AdapterError error)
            {
                throw ListingRetry.NarrowToPermittedCodes(error);
            }
        }

        return surfaces;
    }

    /// <summary>
    /// Seam: the UI Automation root read (<c>root_from_hwnd</c>) and the modal-sheet predicate
    /// (<c>window_is_modal_sheet</c>) are not yet ported. Until they are, a sheet probe reports not-supported rather than
    /// guessing at a surface.
    /// </summary>
    private static bool IsModalSheet(IntPtr handle, Deadline deadline) =>
        throw AdapterError.NotSupported("sheet surface signal");

    private static bool HasOwner(IntPtr handle) => NativeOwner.GetWindow(handle, NativeOwner.GwOwner) != IntPtr.Zero;

    private readonly record struct SurfaceTarget(ProcessId Pid, string Name, string Instance);

    private static class NativeOwner
    {
        internal const uint GwOwner = 4;

        [DllImport("user32.dll", EntryPoint = "GetWindow")]
        internal static extern IntPtr GetWindow(IntPtr window, uint command);
    }
}
