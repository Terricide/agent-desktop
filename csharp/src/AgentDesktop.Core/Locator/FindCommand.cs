using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Locator;

/// <summary>Runs a find: capture one observation of the app, walk it depth-first, and return
/// the nodes the query matches. A node that qualifies carries the ref it was given, so a
/// caller can act on the match with no second read. Running out of budget is a complete answer
/// that says it is partial, never a failure.</summary>
public sealed class FindCommand
{
    private const long LocatorTimeoutMs = 5_000;

    private readonly SnapshotCommand _snapshots;

    private readonly PlatformAdapter _adapter;

    public FindCommand(PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions)
    {
        _adapter = adapter;
        _snapshots = new SnapshotCommand(adapter, snapshots, sessions);
    }

    public EnvelopeResponse Run(FindArgs args, string? sessionId)
    {
        var mode = ValidateMode(args.Selection);
        if (mode is not null)
        {
            return Envelope.Err("find", mode);
        }

        var predicates = ParseStates(args.StateFlags);
        if (predicates.Refusal is not null)
        {
            return Envelope.Err("find", predicates.Refusal);
        }

        var app = args.Scope.App;
        var windowId = args.Scope.WindowId;
        if (string.IsNullOrWhiteSpace(app) && string.IsNullOrWhiteSpace(windowId))
        {
            return Envelope.Err("find", AdapterError.InvalidArgs("find needs --app <name> or --window-id <id>")
                .WithSuggestion("Run 'list-apps' or 'list-windows' and retry with the scope you observed"));
        }

        var deadline = args.TimeoutMs is long timeout ? Deadline.After(timeout) : Deadline.After(LocatorTimeoutMs);
        var captured = CaptureScope(app, windowId, sessionId, deadline);

        var query = LocatorQueryFrom(args, predicates.Predicate!);
        var matches = Collect(query, captured.Root, new List<string>());
        var roles = RolesPresent(captured.Root);
        return Envelope.Ok("find", Select(args.Selection, matches, roles, captured, query));
    }

    /// <summary>The surface find reads. An app name is resolved to its process; a window id is
    /// the whole scope, since the window carries its own app, pid, and process instance.</summary>
    private SnapshotCommand.CapturedTree CaptureScope(string? app, string? windowId, string? sessionId, Deadline deadline)
    {
        if (string.IsNullOrWhiteSpace(app))
        {
            var window = _adapter.ListWindows(new WindowFilter(), deadline).FirstOrDefault(w => w.Id == windowId);
            if (window is null)
            {
                throw new AdapterError(ErrorCode.WindowNotFound, $"Window '{windowId}' was not found")
                    .WithSuggestion("Run 'list-windows' to see what is open");
            }

            return _snapshots.CaptureWindow(window, sessionId, TreeOptions.Default(), SnapshotCommand.MaxObservationNodes, deadline);
        }

        var apps = _adapter.ListAppsScoped(app, null, deadline);
        if (apps.Count == 0)
        {
            throw new AdapterError(ErrorCode.AppNotFound, $"Application '{app}' is not running").Unsent();
        }

        return _snapshots.Capture(
            apps[0],
            sessionId,
            TreeOptions.Default(),
            SnapshotCommand.MaxObservationNodes,
            new WindowScope { WindowId = windowId },
            deadline);
    }

    /// <summary>The query the flags describe. A name, description, value, or text is normalized
    /// once, so the needle is the form a comparison uses; a role is folded to its canonical
    /// name so a shorthand like `img` reaches the role the product reports.</summary>
    private static LocatorQuery LocatorQueryFrom(FindArgs args, List<StatePredicate> predicates) => new()
    {
        Identity = new IdentityPredicate
        {
            Role = args.Filter.Role,
            Name = args.Filter.Name is not null ? SearchText.Normalize(args.Filter.Name) : null,
            Description = args.Filter.Description is not null ? SearchText.Normalize(args.Filter.Description) : null,
            NativeId = args.Filter.NativeId,
            Value = args.Filter.Value is not null ? SearchText.Normalize(args.Filter.Value) : null,
        },
        HasText = args.Filter.Text is not null ? SearchText.Normalize(args.Filter.Text) : null,
        Exact = args.Filter.Exact,
        States = predicates,
    };

    /// <summary>At most one result-shaping mode is accepted, so a request that asks for two is
    /// refused before the application is read.</summary>
    private static AdapterError? ValidateMode(FindSelectionArgs selection)
    {
        var modes = new[] { selection.Count, selection.First, selection.Last, selection.Nth is not null };
        if (modes.Count(mode => mode) > 1 || (modes.Any(mode => mode) && selection.Limit is not null))
        {
            return AdapterError.InvalidArgs("find accepts only one result-shaping mode")
                .WithSuggestion("Use one of --count, --first, --last, --nth, or --limit.");
        }

        return null;
    }

    /// <summary>The state flags as predicates, or the refusal the first bad token earns. The
    /// vocabulary is named so a caller can fix the query rather than read a list.</summary>
    private static (List<StatePredicate> Predicate, AdapterError? Refusal) ParseStates(List<string> flags)
    {
        var predicates = new List<StatePredicate>();
        foreach (var flag in flags)
        {
            var outcome = StatePredicateParser.Parse(flag);
            if (outcome.Refusal is not null)
            {
                return (predicates, AdapterError.InvalidArgs(outcome.Refusal)
                    .WithSuggestion($"Use one of: {string.Join(", ", TreeStates.Vocabulary)}"));
            }

            predicates.Add(outcome.Predicate!);
        }

        return (predicates, null);
    }

    /// <summary>Every node the query matches, in depth-first order, each with the path of its
    /// ancestors so a caller can tell which part of the tree a match sits in.</summary>
    private static List<Match> Collect(LocatorQuery query, AccessibilityNode node, List<string> path)
    {
        var found = new List<Match>();
        if (NodeMatching.Matches(query, node))
        {
            found.Add(new Match(node, new List<string>(path)));
        }

        var label = NodeLabel(node);
        var below = new List<string>(path) { label };
        foreach (var child in node.Children)
        {
            found.AddRange(Collect(query, child, below));
        }

        return found;
    }

    private static string NodeLabel(AccessibilityNode node)
    {
        var name = node.Identity.Name.Value;
        var value = node.Identity.Value.Value;
        var label = string.IsNullOrWhiteSpace(name) ? value : name;
        return string.IsNullOrWhiteSpace(label) ? node.Role : $"{node.Role}:{label}";
    }

    /// <summary>The roles the observation saw, in depth-first order. A caller who asked for a
    /// role the app does not use gets the vocabulary the app does use, not silence.</summary>
    private static List<string> RolesPresent(AccessibilityNode node)
    {
        var roles = new List<string>();
        var stack = new Stack<AccessibilityNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!roles.Contains(current.Role))
            {
                roles.Add(current.Role);
            }

            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }

        return roles;
    }

    private static JsonObject Select(FindSelectionArgs selection, List<Match> matches, List<string> roles, SnapshotCommand.CapturedTree captured, LocatorQuery query)
    {
        var data = new JsonObject();
        if (selection.Count)
        {
            data["count"] = matches.Count;
            return data;
        }

        var chosen = FindSelection.Pick(selection, matches);
        if (selection.First || selection.Last || selection.Nth is not null)
        {
            if (chosen.Count > 0)
            {
                data["match"] = MatchJson(chosen[0]);
            }

            AttachRoles(data, chosen.Count == 0, query, roles);
            AttachSnapshot(data, captured);
            return data;
        }

        data["matches"] = new JsonArray(chosen.Select(match => MatchJson(match)).ToArray());
        data["total_matches"] = matches.Count;
        data["truncated"] = !captured.Complete && matches.Count > chosen.Count;
        AttachRoles(data, chosen.Count == 0, query, roles);
        AttachSnapshot(data, captured);
        return data;
    }

    /// <summary>One match as the response writes it, in the field order the contract declares.
    /// A match with a ref never carries bounds; a match without one is readable text with no
    /// action target, so it carries bounds when the platform reported them.</summary>
    private static JsonObject MatchJson(Match match)
    {
        var node = match.Node;
        var json = new JsonObject();
        if (node.RefId is not null)
        {
            json["ref_id"] = node.RefId;
        }

        json["role"] = node.Role;
        var name = node.Identity.Name.Value;
        if (!string.IsNullOrWhiteSpace(name))
        {
            json["name"] = name;
        }

        var value = node.Identity.Value.Value;
        if (!string.IsNullOrWhiteSpace(value))
        {
            json["value"] = value;
        }

        json["states"] = new JsonArray(node.Presentation.States.Select(state => JsonValue.Create(state)).ToArray());
        json["interactive"] = InteractiveRoles.IsInteractive(node.Role);
        json["path"] = new JsonArray(match.Path.Select(label => JsonValue.Create(label)).ToArray());

        if (node.RefId is null && node.Presentation.Bounds is { } bounds)
        {
            json["bounds"] = new JsonObject
            {
                ["x"] = bounds.X,
                ["y"] = bounds.Y,
                ["width"] = bounds.Width,
                ["height"] = bounds.Height,
            };
        }

        return json;
    }

    /// <summary>When a role query matches nothing, the roles the tree does carry.</summary>
    private static void AttachRoles(JsonObject data, bool empty, LocatorQuery query, List<string> roles)
    {
        if (!empty || query.Identity.Role is null)
        {
            return;
        }

        data["roles_present"] = new JsonArray(roles.Select(role => JsonValue.Create(role)).ToArray());
    }

    private static void AttachSnapshot(JsonObject data, SnapshotCommand.CapturedTree captured) =>
        data["snapshot_id"] = captured.Map.SnapshotId;
}
