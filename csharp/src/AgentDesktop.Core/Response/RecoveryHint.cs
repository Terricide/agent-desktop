namespace AgentDesktop.Core.Response;

/// <summary>The structured recovery hint the envelope carries. Appears only when retry is safe.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial record RecoveryHint(
    string Strategy,
    bool Retryable,
    bool RequiresFreshSnapshot,
    long? RetryAfterMs)
{
    public static RecoveryHint? For(ErrorCode code, DeliverySemantics disposition, string? details)
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

    private static bool ProcessStateExited(string? details) =>
        details is not null && details.Contains("\"process_state\":\"exited\"");
}
