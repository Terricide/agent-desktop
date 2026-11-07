using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Refs;
using Action = AgentDesktop.Core.Actions.Action;
using AdapterError = AgentDesktop.Core.Response.AdapterError;
using ErrorCode = AgentDesktop.Core.Response.ErrorCode;
using Envelope = AgentDesktop.Core.Response.Envelope;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Refs;

/// <summary>Runs one ref action: parse the qualified ref, load the capture proof, re-identify the
/// element, dispatch, and return the adapter's report as data. Zero live matches is STALE_REF,
/// two or more is AMBIGUOUS_TARGET, and control values and bounds are never identity.</summary>
public sealed class RefActionCommand
{
    public RefActionCommand(PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions)
    {
        _adapter = adapter;
        _snapshots = snapshots;
        _sessions = sessions;
    }

    private readonly PlatformAdapter _adapter;

    private readonly SnapshotStore _snapshots;

    private readonly SessionStore _sessions;

    public EnvelopeResponse Run(string command, string qualified, Action action, string? sessionId)
    {
        var deadline = Deadline.Standard();
        var parts = qualified.Split(':');
        if (parts.Length != 2 || !parts[0].StartsWith('@'))
        {
            return Envelope.Err(command, AdapterError.InvalidArgs(
                $"Ref '{qualified}' is not snapshot-qualified. Use @<snapshot_id>:eN"));
        }

        var map = _snapshots.Load(parts[0].Substring(1), _sessions.Namespace(sessionId));
        if (map is null)
        {
            return Envelope.Err(command, new AdapterError(
                ErrorCode.SnapshotNotFound,
                $"Snapshot '{parts[0]}' is not in this session's namespace"));
        }

        var entry = map.Find(qualified);
        if (entry is null)
        {
            return Envelope.Err(command, AdapterError.Stale(qualified));
        }

        if (!entry.Capabilities.UniqueAtCapture)
        {
            return Envelope.Err(command, AdapterError.AmbiguousTarget(
                $"Ref {qualified} was not unique in the observation it was captured from"));
        }

        var handle = _adapter.ResolveElementStrict(entry, deadline);
        using var lease = new InteractionLease(deadline, InteractionPolicy.Unspecified);
        var result = _adapter.ExecuteAction(handle, new ActionRequest(action, false), lease);

        var data = new JsonObject
        {
            ["action"] = result.Action,
            ["delivery"] = result.Disposition.Delivery.AsString(),
            ["retry"] = result.Disposition.Retry.AsString(),
        };

        if (result.Steps.Count > 0)
        {
            var steps = new JsonArray();
            foreach (var step in result.Steps)
            {
                JsonNode member = new JsonObject
                {
                    ["label"] = step.Label,
                    ["outcome"] = step.Outcome,
                    ["mechanism"] = step.Mechanism,
                };
                steps.Add(member);
            }

            data["steps"] = steps;
        }

        return Envelope.Ok(command, data);
    }
}
