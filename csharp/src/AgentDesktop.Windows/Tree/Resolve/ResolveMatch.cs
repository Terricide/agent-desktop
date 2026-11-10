using System.Text.Json.Nodes;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Tree.Resolve;

/// <summary>Builds the stale-ref diagnoses a resolution attempt reports when no live element matches a stored ref.</summary>
internal static class ResolveMatch
{
    internal static AdapterError BuildStaleRefError(RefEntry entry)
    {
        var error = AdapterError.StaleBecause("Stored ref does not match any live element");
        var defaultRetryable = error.PermitsRetryByDefault;
        return error.WithDetails(new JsonObject
        {
            ["kind"] = JsonValue.Create("resolve_no_candidate"),
            ["complete"] = JsonValue.Create(true),
            ["retryable"] = JsonValue.Create(defaultRetryable),
        });
    }

    /// <summary>The terminal twin of <see cref="BuildStaleRefError"/>: the ref's owning process has
    /// itself exited, so no re-attempt within this deadline can ever find a live
    /// candidate. Stamped <c>retryable: false</c> so core's poll loop settles on the
    /// first attempt instead of re-running resolution to the outer deadline and
    /// reporting a bare <c>TIMEOUT</c> that discards the diagnosis (A14-4 measured
    /// ~5s at the default budget for a defect this stamp settles in one pass).
    /// <c>ErrorCode.StaleRef</c> on a <c>not_delivered</c> disposition already carries the
    /// <c>refresh_snapshot_then_retry_original</c> recovery hint (the envelope's recovery
    /// mapping), so nothing further needs attaching here.</summary>
    internal static AdapterError BuildOwningProcessExitedError(RefEntry entry)
    {
        return AdapterError.StaleBecause("Stored ref's owning process has exited").WithDetails(new JsonObject
        {
            ["kind"] = JsonValue.Create("resolve_owning_process_exited"),
            ["complete"] = JsonValue.Create(true),
            ["retryable"] = JsonValue.Create(false),
        });
    }
}
