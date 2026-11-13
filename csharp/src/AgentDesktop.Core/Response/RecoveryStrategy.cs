namespace AgentDesktop.Core.Response;

/// <summary>The strategy names the envelope puts in error.recovery.strategy.</summary>
public static class RecoveryStrategy
{
    public const string RefreshSnapshotThenRetryOriginal = "refresh_snapshot_then_retry_original";

    public const string InspectStateThenRetryOriginal = "inspect_state_then_retry_original";

    public const string RequestExplicitPolicyThenRetryOriginal = "request_explicit_policy_then_retry_original";
}
