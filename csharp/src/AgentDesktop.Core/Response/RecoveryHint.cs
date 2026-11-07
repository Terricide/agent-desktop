using System.Text.Json.Nodes;

namespace AgentDesktop.Core.Response;

/// <summary>The structured recovery hint the envelope carries. Appears only when retry is safe.</summary>
public sealed record RecoveryHint(
    string Strategy,
    bool Retryable,
    bool RequiresFreshSnapshot,
    long? RetryAfterMs)
{
    public JsonNode ToNode()
    {
        var hint = new JsonObject();
        hint["strategy"] = Strategy;
        hint["retryable"] = Retryable;
        hint["requires_fresh_snapshot"] = RequiresFreshSnapshot;

        if (RetryAfterMs.HasValue)
        {
            hint["retry_after_ms"] = RetryAfterMs.Value;
        }

        return hint;
    }
}

public static class RecoveryHints
{
    /// <summary>Selects the strategy from the code and disposition; nothing is offered when retry is not safe.</summary>
    public static RecoveryHint? For(ErrorCode code, DeliverySemantics disposition, JsonNode? details)
    {
        if (disposition.Retry != Retry.Safe)
        {
            return null;
        }

        if (code == ErrorCode.StaleRef && ProcessStateExited(details))
        {
            return new RecoveryHint(RecoveryStrategy.InspectStateThenRetryOriginal, true, true, null);
        }

        if (code == ErrorCode.StaleRef || code == ErrorCode.SnapshotNotFound)
        {
            return new RecoveryHint(RecoveryStrategy.RefreshSnapshotThenRetryOriginal, true, true, null);
        }

        if (code == ErrorCode.PolicyDenied)
        {
            return new RecoveryHint(RecoveryStrategy.RequestExplicitPolicyThenRetryOriginal, true, false, null);
        }

        if (code == ErrorCode.AppUnresponsive || code == ErrorCode.Timeout)
        {
            return new RecoveryHint(RecoveryStrategy.InspectStateThenRetryOriginal, true, true, 250);
        }

        return null;
    }

    private static bool ProcessStateExited(JsonNode? details)
    {
        if (details is not JsonObject obj)
        {
            return false;
        }

        if (!obj.TryGetPropertyValue("process_state", out var state))
        {
            return false;
        }

        return state is JsonValue value && value.TryGetValue(out string? text) && text == "exited";
    }
}
