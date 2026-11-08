using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Snapshot;

/// <summary>Runs one snapshot: find the app, pick its window, observe the tree inside the
/// budget, allocate refs depth-first, persist the refmap, and return the envelope. Running
/// out of budget is a complete answer that says it is partial, never a failure.</summary>
public sealed class SnapshotCommand
{
    /// <summary>The ceiling on nodes one observation may walk, shared with the adapter's budget.</summary>
    public const int MaxObservationNodes = 50_000;

    public SnapshotCommand(PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions)
    {
        _adapter = adapter;
        _snapshots = snapshots;
        _sessions = sessions;
    }

    private readonly PlatformAdapter _adapter;

    private readonly SnapshotStore _snapshots;

    private readonly SessionStore _sessions;

    public EnvelopeResponse Run(string app, string? sessionId, TreeOptions options, int maxNodes, WindowScope? scope = null)
    {
        var deadline = Deadline.Standard();
        var apps = _adapter.ListAppsScoped(app, null, deadline);
        if (apps.Count == 0)
        {
            return Envelope.Err("snapshot", new AdapterError(ErrorCode.AppNotFound, $"Application '{app}' is not running").Unsent());
        }

        var window = PickWindow(apps[0], scope ?? new WindowScope(), deadline);
        var budget = new SnapshotBudget(Math.Min(maxNodes, MaxObservationNodes));
        var observed = _adapter.ObserveTree(
            new ObservationRoot(window, null, options.Surface),
            new ObservationRequest(options.WithRefIdentityBounds(), deadline));
        var map = new RefMap(SnapshotId.New());
        var root = Assign(observed.Root, window, map, budget, options);
        var complete = observed.Complete && budget.IsComplete;
        FinalizeCaptureUniqueness(map, complete);
        _snapshots.Save(map, _sessions.Namespace(sessionId));

        return Envelope.Ok("snapshot", Data(apps[0].Name, map, root, window, complete, budget));
    }

    /// <summary>Records whether each captured ref was uniquely identified over a complete observation.</summary>
    private static void FinalizeCaptureUniqueness(RefMap map, bool complete)
    {
        var entries = map.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string? reason = !complete ? "observation was truncated" : null;
            if (reason is null)
            {
                for (var j = 0; j < entries.Count; j++)
                {
                    if (i != j && entry.Identity.MatchesCapture(entries[j].Identity))
                    {
                        reason = "multiple elements share captured identity";
                        break;
                    }
                }
            }

            if (reason is not null)
            {
                entries[i] = entry with
                {
                    Identity = entry.Identity with { UniquenessReason = reason },
                    Capabilities = entry.Capabilities with { UniqueAtCapture = false },
                    Scope = entry.Scope with { CompleteObservation = complete },
                    UniquenessReason = reason,
                };
            }
        }
    }

    private WindowInfo PickWindow(AppInfo app, WindowScope scope, Deadline deadline)
    {
        var windows = _adapter.ListWindows(new WindowFilter { App = app.Name }, deadline);
        if (windows.Count == 0)
        {
            return WindowInfo.ForProcess(app);
        }

        return WindowLookup.Select(windows, scope, app.Name);
    }

    /// <summary>The command's object, with its keys in sorted order, as the envelope writes
    /// every object. `window` is present only for a window-owned surface.</summary>
    private static JsonObject Data(string app, RefMap map, AccessibilityNode root, WindowInfo window, bool complete, SnapshotBudget budget)
    {
        var writer = new EnvelopeWriter();
        TreeJson.Write(writer, root);
        var fields = new Dictionary<string, JsonNode>
        {
            ["app"] = app,
            ["complete"] = complete,
            ["ref_count"] = map.Count,
            ["snapshot_id"] = map.SnapshotId,
            ["tree"] = JsonNode.Parse(writer.Text())!,
        };

        if (!complete)
        {
            fields["nodes_observed"] = budget.NodesObserved;
            fields["truncated"] = true;
        }

        if (window.Id.Length > 0)
        {
            fields["window"] = new JsonObject { ["id"] = window.Id, ["title"] = window.Title };
        }

        var data = new JsonObject();
        foreach (var key in fields.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            data[key] = fields[key];
        }

        return data;
    }

    private static AccessibilityNode Assign(AccessibilityNode node, WindowInfo window, RefMap map, SnapshotBudget budget, TreeOptions options)
    {
        var qualified = InteractiveRoles.QualifiesForRef(node.Role, new HashSet<string>(node.Presentation.AvailableActions))
            ? Qualified(map.Add(Capture(node, window, map)), map.SnapshotId)
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

            var collapsible = IsCollapsible(child, options);
            var assigned = Assign(child, window, map, budget, options);
            if (collapsible)
            {
                if (assigned.Children.Count > 0)
                {
                    children.Add(assigned.Children[0]);
                }

                continue;
            }

            if (options.InteractiveOnly && IsNoise(assigned))
            {
                continue;
            }

            children.Add(assigned);
        }

        return node with
        {
            RefId = qualified,
            Children = children,
            SubtreeTruncated = node.SubtreeTruncated || cut,
            Presentation = options.IncludeBounds ? node.Presentation : node.Presentation with { Bounds = null },
        };
    }

    /// <summary>A ref in the tree is snapshot-qualified, the form the CLI accepts as input.
    /// The bare form is the refmap key only.</summary>
    private static string Qualified(string bare, string snapshotId) => "@" + snapshotId + ":" + bare;

    /// <summary>A child `--compact` collapses: one that carries no ref and has a single
    /// child, so the child says what the parent says.</summary>
    private static bool IsCollapsible(AccessibilityNode child, TreeOptions options) =>
        options.Compact
        && child.Role == "group"
        && child.RefId is null
        && !InteractiveRoles.QualifiesForRef(child.Role, new HashSet<string>(child.Presentation.AvailableActions))
        && string.IsNullOrWhiteSpace(child.Identity.Name)
        && string.IsNullOrWhiteSpace(child.Identity.Value)
        && string.IsNullOrWhiteSpace(child.Identity.Description)
        && child.Identity.NativeId is null
        && string.IsNullOrWhiteSpace(child.Presentation.Hint)
        && child.Presentation.States.Count == 0
        && child.Presentation.AvailableActions.Count == 0
        && (options.IncludeBounds ? child.Presentation.Bounds is null : true)
        && child.ChildrenCount is null
        && child.Children.Count == 1;

    /// <summary>A child `--interactive-only` drops: no ref, no qualifying role, and nothing
    /// below it to keep.</summary>
    private static bool IsNoise(AccessibilityNode child) =>
        child.RefId is null
        && !InteractiveRoles.IsInteractive(child.Role)
        && child.Children.Count == 0
        && child.ChildrenCount is null;

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
            RefId = "e" + (map.Count + 1),
            ObservedToken = node.Identity.RetainedObject,
        },
        Scope = new RefScope { WindowId = window.Id, CompleteObservation = true },
    };
}
