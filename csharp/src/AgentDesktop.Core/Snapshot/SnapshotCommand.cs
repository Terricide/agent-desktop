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

    /// <summary>The budget one snapshot gets, the same 3 s Rust gives it. A dense window runs out
    /// of this budget and answers with the tree it saw, marked partial. A longer budget walks the
    /// whole window where Rust stops, so the two trees are not the same tree.</summary>
    public const long DefaultSnapshotTimeoutMs = 3_000;

    /// <summary>A captured tree: the refmap it published, the ref-allocated tree, the window it
    /// was captured from, and whether the observation was complete.</summary>
    public sealed record CapturedTree(RefMap Map, AccessibilityNode Root, WindowInfo Window, bool Complete, SnapshotBudget Budget);

    public SnapshotCommand(PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions)
    {
        _adapter = adapter;
        _snapshots = snapshots;
        _sessions = sessions;
    }

    private readonly PlatformAdapter _adapter;

    private readonly SnapshotStore _snapshots;

    private readonly SessionStore _sessions;

    public EnvelopeResponse Run(string? app, string? sessionId, TreeOptions options, int maxNodes, WindowScope? scope = null, long? timeoutMs = null)
    {
        var deadline = Deadline.After(timeoutMs ?? DefaultSnapshotTimeoutMs);
        var window = ResolveSurface(string.IsNullOrEmpty(app) ? null : app, scope?.WindowId, deadline);
        var captured = CaptureWindow(window, sessionId, options, maxNodes, deadline);
        return Envelope.Ok("snapshot", Data(window.App, captured.Map, captured.Root, captured.Window, captured.Complete, captured.Budget));
    }

    /// <summary>The window the snapshot reads: the window id the caller named, a named app's
    /// window, or the window the desktop reports as focused. The window carries its own app, pid,
    /// and process instance, so the envelope reports the app that owns the window.</summary>
    private WindowInfo ResolveSurface(string? app, string? windowId, Deadline deadline)
    {
        var windows = _adapter.ListWindows(new WindowFilter
        {
            FocusedOnly = app is null && windowId is null,
            App = app,
        }, deadline);

        if (windowId is not null)
        {
            var match = windows.FirstOrDefault(w => w.Id == windowId);
            if (match is null)
            {
                throw new AdapterError(ErrorCode.WindowNotFound, $"No window with id {windowId}")
                    .WithSuggestion("Run 'list-windows' to see available window IDs.").Unsent();
            }

            return match;
        }

        if (windows.Count == 0 && app is not null)
        {
            throw (IsRunning(app) ? AdapterError.AppHasNoWindow(app) : ExactIdentityNotFound(app)).Unsent();
        }

        return WindowLookup.SelectWindow(windows, NoWindowFor(app), "More than one window matches the target");
    }

    private static AdapterError ExactIdentityNotFound(string app) =>
        new AdapterError(ErrorCode.AppNotFound, $"Application '{app}' was not found with exact process identity")
            .WithSuggestion("Application name as reported by list-apps");

    private static AdapterError NoWindowFor(string? app) => app is not null
        ? AdapterError.AppHasNoWindow(app)
        : new AdapterError(ErrorCode.WindowNotFound, "No focused window found");

    /// <summary>One observation, ref-allocated and persisted, without an envelope. `find` runs
    /// the same capture so the refs it publishes are proven by the one observation it took, and
    /// a snapshot and a find over the same app see the same tree.</summary>
    public CapturedTree Capture(AppInfo app, string? sessionId, TreeOptions options, int maxNodes, WindowScope scope, Deadline deadline)
    {
        var window = PickWindow(app, scope, deadline);
        return CaptureWindow(window, sessionId, options, maxNodes, deadline);
    }

    /// <summary>A capture scoped to a window the caller named, without an app to resolve. A
    /// window id is the whole scope: the window carries its own app, pid, and process instance.</summary>
    public CapturedTree CaptureWindow(WindowInfo window, string? sessionId, TreeOptions options, int maxNodes, Deadline deadline)
    {
        var budget = new SnapshotBudget(Math.Min(maxNodes, MaxObservationNodes));
        var effective = WithSkeletonDepth(options);
        var observed = _adapter.ObserveTree(
            new ObservationRoot.Window(window),
            new ObservationRequest(effective.WithRefIdentityBounds(), deadline));
        var map = new RefMap(SnapshotId.New());
        var census = new CaptureCensus(observed.Root, observed.Complete);
        var context = new AllocationContext(
            window,
            map,
            budget,
            effective,
            census,
            SkeletonAnchors.Plan(observed.Root, census),
            observed.Complete);
        var root = Assign(observed.Root, context, []);
        var complete = observed.Complete && budget.IsComplete;
        FinalizeCaptureUniqueness(map, complete);
        _snapshots.Save(map, _sessions.Namespace(sessionId));
        return new CapturedTree(map, root, window, complete, budget);
    }

    /// <summary>Whether the process inventory names the app, so the app is running and its window
    /// has not appeared yet.</summary>
    private bool IsRunning(string app) => _adapter.ListAppsScoped(app, null, Deadline.Standard()).Count > 0;

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

    /// <summary>A skeleton is an overview, so it never presents more than three levels. The
    /// depth the walk is given is the caller's depth capped at that ceiling, which is what makes
    /// a skeleton a shallow sweep rather than a filtered full tree.</summary>
    private const byte SkeletonDepthCeiling = 3;

    private static TreeOptions WithSkeletonDepth(TreeOptions options) =>
        options.Skeleton ? options with { MaxDepth = (byte)Math.Min(options.MaxDepth, SkeletonDepthCeiling) } : options;

    /// <summary>What the walk needs to allocate refs: the window, the map being built, the budget,
    /// the options, the one whole-window observation counted for proof, the frames the walk
    /// planned from it, and whether the read covered the whole window.</summary>
    private sealed record AllocationContext(
        WindowInfo Window,
        RefMap Map,
        SnapshotBudget Budget,
        TreeOptions Options,
        CaptureCensus Census,
        HashSet<string> Anchors,
        bool Complete);

    /// <summary>Walks the tree in source order, so a node's path is its index chain from the root.
    /// A ref is allocated for a node that qualifies, and the path is the frame key the capture
    /// planned, so the frame covering a subtree is keyed by that subtree's position.</summary>
    private static AccessibilityNode Assign(AccessibilityNode node, AllocationContext context, List<int> path)
    {
        var qualified = QualifiesForRef(node, path, context)
            ? Qualified(context.Map.Add(Capture(node, path, context)), context.Map.SnapshotId)
            : null;

        var children = new List<AccessibilityNode>();
        var cut = false;
        for (var index = 0; index < node.Children.Count; index++)
        {
            if (!context.Budget.TryObserve())
            {
                cut = true;
                break;
            }

            path.Add(index);
            var collapsible = IsCollapsible(node.Children[index], context.Options);
            var assigned = Assign(node.Children[index], context, path);
            path.RemoveAt(path.Count - 1);
            if (collapsible)
            {
                if (assigned.Children.Count > 0)
                {
                    children.Add(assigned.Children[0]);
                }

                continue;
            }

            if (context.Options.InteractiveOnly && IsNoise(assigned))
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
            Presentation = context.Options.IncludeBounds ? node.Presentation : node.Presentation with { Bounds = null },
        };
    }

    /// <summary>A node gets a ref when its role qualifies, and a skeleton anchor gets one because
    /// it is the node resolution walks through to reach the subtree the capture cannot prove. The
    /// census does not decide eligibility: it records on the entry whether the capture proved the
    /// node unique, which is what a ref action refuses when the proof is not unique.</summary>
    private static bool QualifiesForRef(AccessibilityNode node, List<int> path, AllocationContext context) =>
        InteractiveRoles.QualifiesForRef(node) || context.Anchors.Contains(PathKey(path));

    private static string PathKey(List<int> path) => string.Join('.', path);

    /// <summary>A ref in the tree is snapshot-qualified, the form the CLI accepts as input.
    /// The bare form is the refmap key only.</summary>
    private static string Qualified(string bare, string snapshotId) => "@" + snapshotId + ":" + bare;

    /// <summary>A child `--compact` collapses: one that carries no ref and has a single
    /// child, so the child says what the parent says. A native automation id, a name, a state, or
    /// an advertised action is a descriptor the collapse would lose, so it guards the node.</summary>
    private static bool IsCollapsible(AccessibilityNode child, TreeOptions options) =>
        options.Compact
        && child.Role == "group"
        && child.RefId is null
        && !InteractiveRoles.QualifiesForRef(child.Role, new HashSet<string>(child.Presentation.AvailableActions))
        && Blank(child.Identity.Name)
        && Blank(child.Identity.Value)
        && Blank(child.Identity.Description)
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

    /// <summary>A field only counts as blank when it was read and came back empty. A field
    /// the adapter could not read is not blank, so a node whose name is unread is never
    /// collapsed away.</summary>
    private static bool Blank(LocatorField<string> field) =>
        field.IsKnown() && string.IsNullOrWhiteSpace(field.Value);

    private static RefEntry Capture(AccessibilityNode node, List<int> path, AllocationContext context)
    {
        var window = context.Window;
        var uniqueAtCapture = context.Census.Proves(node);
        var entry = new RefEntry
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
                StableName = FullPredicate.StableNameField(node.Role, node.Identity.Name, node.Identity.Value),
                Description = node.Identity.Description.ToNullableText(),
                WindowKey = window.Id,
                UniqueAtCapture = uniqueAtCapture,
            },
            Geometry = new RefGeometry { Bounds = node.Presentation.Bounds },
            Capabilities = new RefCapabilities
            {
                AvailableActions = IsSkeletonAnchor(node, path, context) ? [] : node.Presentation.AvailableActions,
                UniqueAtCapture = uniqueAtCapture,
            },
            Source = new RefSource
            {
                SnapshotId = context.Map.SnapshotId,
                RefId = "e" + (context.Map.Count + 1),
                ObservedToken = node.Identity.RetainedObject,
                Surface = SnapshotSurface.Window,
                SourceWindowId = NullIfEmpty(window.Id),
                SourceWindowTitle = NullIfEmpty(window.Title),
                SourceApp = NullIfEmpty(window.App),
            },
            Scope = new RefScope { WindowId = window.Id, CompleteObservation = context.Complete },
            UniquenessReason = uniqueAtCapture
                ? null
                : "capture did not prove a unique whole-window match",
        };

        if (!context.Options.IncludeBounds
            && FullPredicate.HasMeaningfulIdentity(
                node.Role,
                KnownText(node.Identity.Name),
                KnownText(node.Identity.Value),
                node.Identity.NativeId?.Value,
                node.Identity.RetainedObject))
        {
            entry = entry with { Geometry = entry.Geometry with { Bounds = null } };
        }

        return entry;
    }

    /// <summary>The frame a node carries when its role does not qualify and the capture planned
    /// it, which is the frame resolution reaches a subtree through. A skeleton anchor records no
    /// advertised actions, because it anchors the subtree it covers and the actions belong to the
    /// elements inside it.</summary>
    private static bool IsSkeletonAnchor(AccessibilityNode node, List<int> path, AllocationContext context) =>
        !InteractiveRoles.QualifiesForRef(node) && context.Anchors.Contains(PathKey(path));

    private static string? KnownText(LocatorField<string> field) =>
        field.IsKnown() ? field.Value : null;

    private static string? NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;
}
