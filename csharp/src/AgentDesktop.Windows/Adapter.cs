using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Actions;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows;

/// <summary>The Windows adapter: UI Automation for observation and element actions, plus the
/// top-level window census and the process table for app and window identity. Every method
/// the adapter does not implement keeps core's refusal, so a surface the platform does not
/// support fails closed.</summary>
public sealed partial class WindowsAdapter : PlatformAdapter
{
    private readonly TreeAdapter _observation = new();

    private readonly ActionsAdapter _actions = new();

    public ObservedTree ObserveTree(ObservationRoot root, ObservationRequest request) =>
        _observation.ObserveTree(root, request);

    public AccessibilityNode GetTree(WindowInfo window, TreeOptions options, Deadline deadline) =>
        _observation.GetTree(window, options, deadline);

    public List<WindowInfo> ListWindows(WindowInfo? _unused, WindowFilter filter, Deadline deadline) =>
        WindowCensus.Collect(filter);

    public List<WindowInfo> ListWindows(WindowFilter filter, Deadline deadline) => WindowCensus.Collect(filter);

    public List<AppInfo> ListApps(Deadline deadline) => AppCensus.Collect().Apps;

    public AppInfo? FocusedApp(Deadline deadline) => FocusedAppReader.Read();

    public NativeHandle ResolveElementStrict(RefEntry entry, Deadline deadline)
    {
        var hr = Client.Create(out var client);
        if (client is null)
        {
            throw UiaFailure.Error(hr, "create a UI Automation client whose calls are bounded");
        }

        using (client)
        {
            return ElementResolve.ResolveStrict(entry, client, deadline);
        }
    }

    public ActionResult ExecuteAction(NativeHandle handle, ActionRequest request, InteractionLease lease) =>
        _actions.ExecuteAction(handle, request, lease);

    public void ScrollIntoView(NativeHandle handle, InteractionLease lease) =>
        _actions.ScrollIntoView(handle, lease);

    /// <summary>The adapter records no element token on observed nodes, so a shallow capture
    /// cannot re-identify its refs from a token and needs a whole-window read to prove them.
    /// Matches the Rust Windows adapter, which leaves <c>retained_object</c> unset.</summary>
    public bool RetainsElementTokens() => false;
}
