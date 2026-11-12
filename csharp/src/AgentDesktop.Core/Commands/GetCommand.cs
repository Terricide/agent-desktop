using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Commands;

/// <summary>Reads one property of a captured ref. Resolution is the same strict re-identification
/// every ref action uses, so a ref the capture did not prove unique is refused before the
/// application is read. A live read the platform cannot do, or answered nothing, falls back to the
/// field the capture recorded; a property the capture never recorded reports the platform's
/// refusal. Bounds say which of the two the answer came from.</summary>
public sealed class GetCommand
{
    private readonly PlatformAdapter _adapter;
    private readonly SnapshotStore _snapshots;
    private readonly SessionStore _sessions;

    public GetCommand(PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions)
    {
        _adapter = adapter;
        _snapshots = snapshots;
        _sessions = sessions;
    }

    /// <summary>Runs one get. The property selects which projection of the live read answers.</summary>
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
            return Envelope.Err(command, new AdapterError(
                ErrorCode.SnapshotNotFound,
                $"Snapshot '{snapshotId}' is not in this session's namespace"));
        }

        var entry = map.Find(qualifiedRef);
        if (entry is null)
        {
            return Envelope.Err(command, AdapterError.Stale(qualifiedRef));
        }

        using var handle = _adapter.ResolveElementStrict(entry, deadline);

        return property switch
        {
            "role" => Envelope.Ok("get", Data("role", Text(entry.Identity.Role), null)),
            "title" => Envelope.Ok("get", Data("title", Text(entry.Identity.StableName.Value), null)),
            "text" => ReadText(command, entry, handle, deadline),
            "bounds" => ReadBounds(command, entry, handle, deadline),
            "value" => ReadLiveOnly(command, "value", qualifiedRef,
                () => ValueNode(_adapter.GetLiveValue(handle, deadline))),
            "states" => ReadLiveOnly(command, "states", qualifiedRef,
                () => StatesNode(_adapter.GetLiveState(handle, deadline))),
            _ => Envelope.Err(command, AdapterError.InvalidArgs(
                $"'{property}' is not a property. Use text, value, title, bounds, role, states")),
        };
    }

    /// <summary>The text a person reads: for a role whose value is the readable content, the live
    /// value first and the recorded name as fallback; for every other role, the recorded name
    /// first and the live value as fallback.</summary>
    private EnvelopeResponse ReadText(string command, RefEntry entry, NativeHandle handle, Deadline deadline)
    {
        var live = Live(() => NonBlank(_adapter.GetLiveValue(handle, deadline)));
        var recorded = NonBlank(entry.Identity.StableName.Value);
        var readable = RoleText.ValueIsTheReadableText(entry.Identity.Role)
            ? live.Value ?? recorded
            : recorded ?? live.Value;

        return Envelope.Ok(command, Data("text", Text(readable), null));
    }

    private EnvelopeResponse ReadBounds(string command, RefEntry entry, NativeHandle handle, Deadline deadline)
    {
        var live = Live(() => _adapter.GetElementBounds(handle, deadline));
        var bounds = live.Value is not null ? live.Value : entry.Geometry.Bounds;

        return Envelope.Ok(command, Data("bounds", BoundsNode(bounds), live.Available && live.Value is not null));
    }

    /// <summary>A property the capture never recorded has no fallback. A live read the platform
    /// refused is reported as the platform's refusal; a live read that answered nothing is
    /// reported as a live read that found no value, because there is nothing to fall back on.</summary>
    private EnvelopeResponse ReadLiveOnly(string command, string property, string qualifiedRef,
        Func<JsonNode?> read)
    {
        var live = Live(read);
        if (live.Error is not null)
        {
            return Envelope.Err(command, live.Error);
        }

        if (live.Value is null)
        {
            return Envelope.Err(command, new AdapterError(
                ErrorCode.ActionFailed,
                $"The live read of '{property}' for '{qualifiedRef}' returned nothing, and the capture recorded no {property} to fall back on")
                .WithSuggestion("Re-snapshot and read the property from the snapshot tree"));
        }

        return Envelope.Ok(command, Data(property, live.Value, true));
    }

    /// <summary>A live read the platform cannot do, or answered nothing, is not the command's
    /// failure: the caller decides what an unavailable read means for its property.</summary>
    private static LiveResult<T> Live<T>(Func<T> read)
    {
        try
        {
            return new LiveResult<T>(true, read(), null);
        }
        catch (AdapterError error)
        {
            return new LiveResult<T>(false, default, error);
        }
    }

    private sealed record LiveResult<T>(bool Available, T? Value, AdapterError? Error);

    private static JsonObject Data(string property, JsonNode? value, bool? live)
    {
        var data = new JsonObject { ["property"] = property, ["value"] = value };
        if (live is not null)
        {
            data["live"] = live.Value;
        }

        return data;
    }

    private static JsonNode? Text(string? text) => string.IsNullOrEmpty(text) ? null : JsonValue.Create(text);

    /// <summary>An empty value is the adapter's answer, not an unavailable read: a control with no
    /// value text answers "".</summary>
    private static JsonNode? ValueNode(string? text) => text is null ? null : JsonValue.Create(text);

    /// <summary>An element with no states answers an empty list, not a missing read. A state read
    /// the adapter did not return is the unavailable case.</summary>
    private static JsonNode? StatesNode(ElementState? state)
    {
        if (state is null)
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var name in state.States)
        {
            array.Add((JsonNode)JsonValue.Create(name)!);
        }

        return array;
    }

    private static JsonNode? BoundsNode(Rect? rect)
    {
        if (rect is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["x"] = rect.Value.X,
            ["y"] = rect.Value.Y,
            ["width"] = rect.Value.Width,
            ["height"] = rect.Value.Height,
        };
    }

    private static string? NonBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

/// <summary>Which roles read their content from the value: the editable text roles. Every other
/// role reads its content from the accessible name.</summary>
public static class RoleText
{
    private static readonly string[] ValueRoles = ["combobox", "datefield", "listbox", "textfield", "timefield"];

    public static bool ValueIsTheReadableText(string role) => ValueRoles.Contains(role);
}
