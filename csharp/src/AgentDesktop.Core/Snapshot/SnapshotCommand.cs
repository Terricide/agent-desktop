using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AdapterError = AgentDesktop.Core.Response.AdapterError;
using ErrorCode = AgentDesktop.Core.Response.ErrorCode;
using Envelope = AgentDesktop.Core.Response.Envelope;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Snapshot;

/// <summary>Runs one snapshot: find the app, observe its window inside the budget, allocate refs
/// depth-first, persist the refmap, and return the envelope. Running out of budget is a complete
/// answer that says it is partial, never a failure.</summary>
public sealed class SnapshotCommand
{
    public SnapshotCommand(PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions)
    {
        _adapter = adapter;
        _snapshots = snapshots;
        _sessions = sessions;
    }

    private readonly PlatformAdapter _adapter;

    private readonly SnapshotStore _snapshots;

    private readonly SessionStore _sessions;

    public EnvelopeResponse Run(string app, string? sessionId, TreeOptions options, int maxNodes)
    {
        var deadline = Deadline.Standard();
        var apps = _adapter.ListAppsScoped(app, null, deadline);
        if (apps.Count == 0)
        {
            return Envelope.Err("snapshot", new AdapterError(ErrorCode.AppNotFound, $"Application '{app}' is not running"));
        }

        var windows = _adapter.ListWindows(new WindowFilter { App = app }, deadline);
        var window = windows.Count > 0 ? windows[0] : WindowInfo.ForProcess(apps[0]);
        var budget = new SnapshotBudget(maxNodes);
        var observed = _adapter.ObserveTree(new ObservationRoot(window, null, options.Surface), new ObservationRequest(options.WithRefIdentityBounds(), deadline));
        var map = new RefMap(NewSnapshotId());
        var root = Assign(observed.Root, window, map, budget);
        var complete = observed.Complete && budget.IsComplete;
        _snapshots.Save(map, _sessions.Namespace(sessionId));

        var writer = new EnvelopeWriter();
        root.WriteJson(writer);
        var data = new JsonObject
        {
            ["app"] = apps[0].Name,
            ["snapshot_id"] = map.SnapshotId,
            ["ref_count"] = map.Count,
            ["complete"] = complete,
        };

        if (!complete)
        {
            data["truncated"] = true;
            data["nodes_observed"] = budget.NodesObserved;
        }

        data["tree"] = JsonNode.Parse(writer.Text());
        return Envelope.Ok("snapshot", data);
    }

    private AccessibilityNode Assign(AccessibilityNode node, WindowInfo window, RefMap map, SnapshotBudget budget)
    {
        var reference = InteractiveRoles.QualifiesForRef(node.Role, new HashSet<string>(node.Presentation.AvailableActions))
            ? map.Add(Capture(node, window, map))
            : null;

        var children = new List<AccessibilityNode>();
        var cut = false;
        foreach (var child in node.Children)
        {
            if (!budget.TryObserve())
            {
                cut = true;
                break;
            }

            children.Add(Assign(child, window, map, budget));
        }

        return node with { RefId = reference, Children = children, SubtreeTruncated = cut };
    }

    private static RefEntry Capture(AccessibilityNode node, WindowInfo window, RefMap map) => new()
    {
        Process = new RefProcess
        {
            Name = window.App,
            Pid = window.Pid,
            Instance = window.ProcessInstance,
        },
        Identity = new RefEntryIdentity
        {
            Role = node.Role,
            NativeId = node.Identity.NativeId?.Value,
            StableName = node.Identity.Name,
            Description = node.Identity.Description,
            WindowKey = window.Id,
        },
        Geometry = new RefGeometry { Bounds = node.Presentation.Bounds },
        Capabilities = new RefCapabilities
        {
            AvailableActions = node.Presentation.AvailableActions,
            UniqueAtCapture = true,
        },
        Source = new RefSource
        {
            SnapshotId = map.SnapshotId,
            RefId = "e" + map.Count,
            ObservedToken = node.Identity.RetainedObject,
        },
        Scope = new RefScope { WindowId = window.Id, CompleteObservation = true },
    };

    private static string NewSnapshotId() => Guid.NewGuid().ToString("N").Substring(0, 8);
}
