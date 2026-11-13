using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Core.Locator;
using AgentDesktop.Windows.System.Menu;
using AgentDesktop.Windows.System.Window;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;
using global::System.Text.Json.Nodes;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// Resolves the element an observation root names, per surface. <c>Window</c> resolves the named window
/// directly; <c>Focused</c> composes the focused-only inventory. Only the surfaces this adapter advertises are
/// handled: core validates the requested surface before the adapter is called.
/// </summary>
internal static class Surfaces
{
    internal static Element SurfaceRoot(ObservationRoot root, SnapshotSurface surface, Deadline deadline)
    {
        switch (root)
        {
            case ObservationRoot.Element element:
                return ElementRoot(element.Handle);
            case ObservationRoot.Application application:
                throw AdapterError.NotSupported("application observation root");
            case ObservationRoot.Window window:
                if (window.Info.IsProcessOwner())
                {
                    throw AdapterError.AppHasNoWindow(window.Info.App);
                }

                return WindowSurfaceRoot(window.Info, surface, deadline);
            default:
                throw AdapterError.NotSupported("observation root");
        }
    }

    private static Element WindowSurfaceRoot(WindowInfo window, SnapshotSurface surface, Deadline deadline)
    {
        switch (surface)
        {
            case SnapshotSurface.Window:
                return RootFromWindowId(window.Id, deadline);
            case SnapshotSurface.Focused:
                return RootFromHwnd(FocusedHwndOf(window.Id), deadline);
            case SnapshotSurface.Sheet:
                return SheetRoot(window.Id, deadline);
            case SnapshotSurface.Menu:
                return RootFromHwnd(LocateMenuRootHandle(window.Pid, deadline), deadline);
            case SnapshotSurface.StartMenu:
            case SnapshotSurface.Taskbar:
            case SnapshotSurface.SystemTray:
            case SnapshotSurface.SystemTrayOverflow:
            case SnapshotSurface.ActionCenter:
                return RootFromWindowId(window.Id, deadline);
            default:
                throw AdapterError.NotSupported("surface");
        }
    }

    private static Element SheetRoot(string windowId, Deadline deadline)
    {
        var element = RootFromHwnd(FocusedHwndOf(windowId), deadline);
        if (WindowIsModalSheet(element))
        {
            return element;
        }

        throw new AdapterError(ErrorCode.WindowNotFound, "The focused window is not a modal sheet");
    }

    private static nint LocateMenuRootHandle(ProcessId pid, Deadline deadline)
    {
        var location = MenuStateLocate.LocateMenu(pid, deadline)
            ?? throw new AdapterError(
                ErrorCode.WindowNotFound,
                "No menu this surface can root was found for this application")
                .WithSuggestion(
                    "A classic Win32 menu can be open without exposing an element to root at, so a "
                    + "menu wait can succeed where this refuses. Read the owning window's own surface instead.");

        return location.RootHandle();
    }

    /// <summary>An element root borrows the UI Automation element a stored ref captured. A handle
    /// that does not carry one is a stale ref, not a platform failure: the ref named something this
    /// platform cannot root at.</summary>
    private static Element ElementRoot(NativeHandle handle) =>
        handle.Downcast<Element>() ?? throw new AdapterError(
            ErrorCode.StaleRef,
            "Element root handle is invalid for this platform")
            .WithDetails(new JsonObject { ["kind"] = "element_root_wrong_payload" });

    private static Element RootFromWindowId(string windowId, Deadline deadline) =>
        RootFromHwnd(WindowOps.ParseHandle(windowId), deadline);

    private static Element RootFromHwnd(nint handle, Deadline deadline) =>
        Automation.RootFromHwnd(handle, deadline);

    /// <summary>
    /// The focused window's HWND, found through the focused-only window inventory the same way
    /// <c>focused_window</c> composes it.
    /// </summary>
    private static nint FocusedHwndOf(string expected)
    {
        nint? focused = null;
        WindowEnum.EnumerateTopLevel(window =>
        {
            if (!WindowEnum.PassesFilter(window))
            {
                return true;
            }

            if (Interop.GetForegroundWindow() == window.Handle)
            {
                focused = window.Handle;
                return false;
            }

            return true;
        });

        if (focused is not { } handle)
        {
            throw new AdapterError(ErrorCode.WindowNotFound, "There is no focused window on this desktop");
        }

        if ($"w-{handle}" != expected)
        {
            throw new AdapterError(
                ErrorCode.WindowNotFound,
                "The requested focused window is no longer the focused window");
        }

        return handle;
    }

    /// <summary>
    /// Whether a window is a modal sheet surface, tested from the window's own <c>WindowIsModal</c> property
    /// before any child is consulted. This classifies a Chromium modal as a <c>Sheet</c> surface, making it
    /// reachable via the sheet surface.
    /// </summary>
    internal static bool WindowIsModalSheet(Element root) =>
        PropertyUia.ReadOne(root, TreeProperty.WindowIsModal) is PropertyOutcome.Known { Value: PropertyValue.Flag(var flag) }
            && flag;
}
