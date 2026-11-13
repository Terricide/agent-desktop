
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Adapter;

/// <summary>Reading the UI: apps, windows, surfaces, trees, live element state, and refs resolved back to live elements. Every method defaults to a refusal, so an adapter implements only what it supports.</summary>
public interface ObservationOps
{
    bool IsApplicationSurface(SnapshotSurface surface) => false;

    AppInfo? FocusedApp(Deadline deadline) => throw AdapterError.NotSupported("focused_app");

    /// <summary>Reports protocol targets that have no screen position without requiring a native read.</summary>
    bool IsNonspatialTarget(NativeHandle handle) => false;

    /// <summary>Reports proven nonspatial targets, including native menus, within the observation deadline.</summary>
    bool IsNonspatialTargetWithDeadline(NativeHandle handle, Deadline deadline) =>
        IsNonspatialTarget(handle);

    ObservedTree ObserveTree(ObservationRoot root, ObservationRequest request) =>
        throw AdapterError.NotSupported("observe_tree");

    List<WindowInfo> ListWindows(WindowFilter filter, Deadline deadline) =>
        throw AdapterError.NotSupported("list_windows");

    List<AppInfo> ListApps(Deadline deadline) => throw AdapterError.NotSupported("list_apps");

    AppInventory ListAppsInventory(Deadline deadline) =>
        new() { Apps = ListApps(deadline), Skipped = new List<string>() };

    List<AppInfo> ListAppsScoped(string name, string? bundleId, Deadline deadline)
    {
        var matched = new List<AppInfo>();
        foreach (var app in ListApps(deadline))
        {
            if (app.MatchesIdentifier(name)
                && (bundleId is null
                    || (app.BundleId is not null && app.BundleId.Equals(bundleId, StringComparison.OrdinalIgnoreCase))))
            {
                matched.Add(app);
            }
        }

        return matched;
    }

    /// <summary>Allows filtering before presentation enrichment. Skipped records describe incomplete evidence within the query.</summary>
    AppInventory QueryAppInventory(AppInventoryQuery query, Deadline deadline)
    {
        if (query.Kind == AppInventoryQueryKind.All)
        {
            return ListAppsInventory(deadline);
        }

        if (query.Kind == AppInventoryQueryKind.Exact)
        {
            return new AppInventory
            {
                Apps = ListAppsScoped(query.Name!, query.BundleId, deadline),
                Skipped = new List<string>(),
            };
        }

        var inventory = ListAppsInventory(deadline);
        inventory.Apps.RemoveAll(app => !query.MatchesDisplayName(app.Name));

        List<AppInfo> aliased;
        try
        {
            aliased = ListAppsScoped(query.Name!, null, deadline);
        }
        catch (AdapterError error) when (error.Code == ErrorCode.PlatformNotSupported)
        {
            aliased = new List<AppInfo>();
        }

        foreach (var app in aliased)
        {
            if (!inventory.Apps.Exists(kept => kept.Pid == app.Pid))
            {
                inventory.Apps.Add(app);
            }
        }

        return inventory;
    }

    AccessibilityNode GetTree(WindowInfo window, TreeOptions options, Deadline deadline) =>
        throw AdapterError.NotSupported("get_tree");

    NativeHandle ResolveElementStrict(RefEntry entry, Deadline deadline) =>
        throw AdapterError.NotSupported("resolve_element_strict");

    /// <summary>Whether this adapter records an element token on observed nodes, so a shallow capture can re-identify its refs without a whole-window read.</summary>
    bool RetainsElementTokens() => false;

    /// <summary>Checks platform context the saved ref's identity predicate does not encode, against the token from the same observation. Must not take a second live read.</summary>
    void ValidateSavedRefContext(RefEntry entry, string? observedToken) { }

    NativeHandle ResolveLocatorAnchor(RefEntry entry, Deadline deadline) =>
        throw AdapterError.NotSupported("resolve_locator_anchor");

    List<SurfaceInfo> ListSurfaces(ProcessIdentity process, Deadline deadline) =>
        throw AdapterError.NotSupported("list_surfaces");

    /// <summary>Reads an already-open OS-chrome surface into the window identity the observation stack consumes. It never raises anything.</summary>
    WindowInfo ResolveShellSurface(SnapshotSurface surface, Deadline deadline) =>
        throw AdapterError.NotSupported("resolve_shell_surface");

    string? GetLiveValue(NativeHandle handle, Deadline deadline) =>
        throw AdapterError.NotSupported("get_live_value");

    /// <summary>Selected text offsets in UTF-16 code units, or unavailable evidence.</summary>
    (int Start, int End)? GetTextSelection(NativeHandle handle, Deadline deadline) =>
        throw AdapterError.NotSupported("get_text_selection");

    ElementState? GetLiveState(NativeHandle handle, Deadline deadline) =>
        throw AdapterError.NotSupported("get_live_state");

    List<string>? GetLiveActions(NativeHandle handle, Deadline deadline) =>
        throw AdapterError.NotSupported("get_live_actions");

    LiveElement GetLiveElement(NativeHandle handle, Deadline deadline) =>
        throw AdapterError.NotSupported("get_live_element");

    Rect? GetElementBounds(NativeHandle handle, Deadline deadline) =>
        throw AdapterError.NotSupported("get_element_bounds");

    /// <summary>Screen bounds of a text element's insertion point: a caret-wide rect as tall as the line it sits on.</summary>
    Rect? GetInsertionCaret(NativeHandle handle, Deadline deadline) =>
        throw AdapterError.NotSupported("get_insertion_caret");

    /// <summary>The insertion point of the focused text element, in a process when given and system-wide otherwise.</summary>
    Rect? GetFocusedInsertionCaret(ProcessId? process, Deadline deadline) =>
        throw AdapterError.NotSupported("get_focused_insertion_caret");

    /// <summary>Fresh pointer geometry and visibility from one observation. The default preserves bounds-first reads and optional state evidence.</summary>
    AgentDesktop.Core.Input.PointerObservation ObservePointer(NativeHandle handle, Deadline deadline)
    {
        var bounds = GetElementBounds(handle, deadline);
        var state = GetLiveState(handle, deadline);
        return new AgentDesktop.Core.Input.PointerObservation
        {
            Bounds = bounds,
            Visible = state?.VisibleWithoutBounds(),
            Complete = bounds is not null,
        };
    }
}
