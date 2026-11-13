using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Commands;

/// <summary>
/// The `is` command: one property of one element, answered from the live element the ref
/// resolves to. Every property carries `applicable`, which names whether the property applies
/// to this element. A property that does not apply reports `result: false` with
/// `applicable: false`, so a false answer never reads as a measured fact.
/// Port of <c>crates/core/src/commands/is_check.rs</c>.
/// </summary>
public sealed class IsCommand
{
    private readonly PlatformAdapter _adapter;
    private readonly SnapshotStore _snapshots;
    private readonly SessionStore _sessions;

    public IsCommand(PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions)
    {
        _adapter = adapter;
        _snapshots = snapshots;
        _sessions = sessions;
    }

    /// <summary>Runs one is. Resolution is the same strict re-identification every ref action
    /// uses, so a ref the capture did not prove unique is refused before the application is
    /// read. A live read the platform cannot do is not the command's failure: it makes the
    /// property inapplicable.</summary>
    public EnvelopeResponse Run(string command, string qualifiedRef, string property, string? sessionId)
    {
        var deadline = Deadline.Standard();
        var parts = qualifiedRef.Split(':');
        if (parts.Length != 2 || !parts[0].StartsWith('@'))
        {
            return Envelope.Err(command, AdapterError.InvalidArgs(
                $"Ref '{qualifiedRef}' is not snapshot-qualified. Use @<snapshot_id>:eN"));
        }

        var snapshotId = parts[0].Substring(1);
        var map = _snapshots.Load(snapshotId, _sessions.Namespace(sessionId));
        if (map is null)
        {
            return Envelope.Err(command, AdapterError.SnapshotNotFound(
                snapshotId,
                $"Snapshot '{snapshotId}' is not in this session's namespace"));
        }

        var entry = map.Find(qualifiedRef);
        if (entry is null)
        {
            return Envelope.Err(command, AdapterError.Stale(qualifiedRef));
        }

        using var handle = _adapter.ResolveElementStrict(entry, deadline);
        var live = Live(() => _adapter.GetLiveState(handle, deadline));
        var bounds = Live(() => _adapter.GetElementBounds(handle, deadline));
        var state = live.Value ?? StateFromRefEntry(entry);
        var statesFromLive = live.Value is not null;

        return property switch
        {
            "visible" => Visible(command, qualifiedRef, entry, state, statesFromLive, bounds),
            "enabled" => Answer(command, property, qualifiedRef, state.Enabled ?? false, state.Enabled.HasValue),
            "focused" => Answer(command, property, qualifiedRef, Has(state.States, TreeStates.Focused), true),
            "checked" => Answer(command, property, qualifiedRef, Has(state.States, TreeStates.Checked),
                CanonicalRoles.IsToggleableRole(entry.Identity.Role)
                    || Has(state.States, TreeStates.Checked)
                    || HasAny(entry.Capabilities.AvailableActions, CheckedApplicability)),
            "expanded" => Answer(command, property, qualifiedRef, Has(state.States, TreeStates.Expanded),
                CanonicalRoles.IsExpandableRole(entry.Identity.Role)
                    || Has(state.States, TreeStates.Expanded)
                    || HasAny(entry.Capabilities.AvailableActions, ExpandedApplicability)),
            "selected" => Answer(command, property, qualifiedRef, Has(state.States, TreeStates.Selected), true),
            _ => Envelope.Err(command, AdapterError.InvalidArgs(
                $"'{property}' is not a property. Use visible, enabled, checked, focused, expanded, selected")),
        };
    }

    private static readonly string[] CheckedApplicability = ["Toggle", "Check", "Uncheck"];

    private static readonly string[] ExpandedApplicability = ["Expand", "Collapse"];

    /// <summary>The actionability gate passes visible for a node whose bounds cannot be read
    /// once the live state says it is not offscreen; the command answers from that same
    /// evidence instead of reporting the property inapplicable. Otherwise the property applies
    /// only when both the bounds and the state came from the live element.</summary>
    private static EnvelopeResponse Visible(string command, string qualifiedRef, RefEntry entry,
        ElementState state, bool statesFromLive, LiveResult<Rect?> bounds)
    {
        var boundsIsLive = bounds.Value is not null;

        if (bounds.Value is null && statesFromLive)
        {
            var proved = state.VisibleWithoutBounds();
            if (proved is not null)
            {
                return Answer(command, "visible", qualifiedRef, proved.Value, true);
            }
        }

        var applicable = boundsIsLive && statesFromLive;
        var rect = bounds.Value ?? entry.Geometry.Bounds;

        return Answer(command, "visible", qualifiedRef,
            applicable && BoundsAreVisible(rect) && !Has(state.States, TreeStates.Hidden) && !Has(state.States, TreeStates.Offscreen),
            applicable);
    }

    private static EnvelopeResponse Answer(string command, string property, string qualifiedRef,
        bool result, bool applicable)
    {
        return Envelope.Ok(command, new JsonObject
        {
            ["property"] = property,
            ["ref"] = qualifiedRef,
            ["result"] = result,
            ["applicable"] = applicable,
        });
    }

    /// <summary>A live read the platform cannot do is not the command's failure. It leaves the
    /// property without evidence, which the caller reports as inapplicable.</summary>
    private static LiveResult<T> Live<T>(Func<T> read)
    {
        try
        {
            return new LiveResult<T>(read(), true);
        }
        catch (AdapterError)
        {
            return new LiveResult<T>(default, false);
        }
    }

    private sealed record LiveResult<T>(T? Value, bool FromLive);

    /// <summary>The state the capture recorded, used when the live read finds nothing. A state
    /// property answers from the capture record, so a control whose live state is unreadable is
    /// not reported as having lost the state it was captured with.</summary>
    private static ElementState StateFromRefEntry(RefEntry entry) => new()
    {
        Role = entry.Identity.Role,
        States = entry.Capabilities.States,
    };

    private static bool Has(List<string> states, string token) => states.Contains(token);

    private static bool HasAny(List<string> actions, string[] tokens) =>
        actions.Any(token => tokens.Contains(token));

    private static bool BoundsAreVisible(Rect? rect) =>
        rect is not null && rect.Value.IsFiniteBounded() && rect.Value.Width > 0.0 && rect.Value.Height > 0.0;
}
