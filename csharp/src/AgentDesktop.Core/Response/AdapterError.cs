using System.Text.Json;

namespace AgentDesktop.Core.Response;

/// <summary>An adapter failure: code, message, and the delivery evidence the command established.</summary>
public class AdapterError : Exception
{
    public ErrorCode Code { get; }

    public string? Suggestion { get; private set; }

    public string? PlatformDetail { get; private set; }

    public System.Text.Json.Nodes.JsonNode? Details { get; private set; }

    public DeliverySemantics Disposition { get; private set; }

    public Retryability Retryability { get; private set; }

    public AdapterError(ErrorCode code, string message) : base(message)
    {
        Code = code;
        Disposition = DeliverySemantics.Unknown();
        Retryability = Retryability.Unspecified;
    }

    public AdapterError WithSuggestion(string suggestion)
    {
        Suggestion = suggestion;
        return this;
    }

    public AdapterError WithPlatformDetail(string detail)
    {
        PlatformDetail = detail;
        return this;
    }

    public AdapterError WithDetails(System.Text.Json.Nodes.JsonNode details)
    {
        var stamp = RetryabilityReader.FromDetails(details);
        if (stamp != Retryability.Unspecified)
        {
            Retryability = stamp;
        }

        Details = details;
        return this;
    }

    public AdapterError WithDetails(string detailsJson)
    {
        var parsed = System.Text.Json.Nodes.JsonNode.Parse(detailsJson);
        return parsed is not null ? WithDetails(parsed) : this;
    }

    public AdapterError WithDisposition(DeliverySemantics disposition)
    {
        Disposition = disposition;
        return this;
    }

    /// <summary>Marks a refusal raised before anything reached the application, keeping stronger evidence.</summary>
    public AdapterError Unsent()
    {
        if (Disposition == DeliverySemantics.Unknown())
        {
            Disposition = DeliverySemantics.NotDelivered();
        }

        return this;
    }

    public bool IsExplicitlyRetryable => Retryability == Retryability.Retryable;

    public bool PermitsRetryByDefault => Retryability != Retryability.NonRetryable;

    /// <summary>The recovery hint the envelope carries. Nothing is offered when retry is not safe.</summary>
    public RecoveryHint? Recovery =>
        PermitsRetryByDefault ? RecoveryHints.For(Code, Disposition, Details) : null;

    /// <summary>An incomplete read an adapter's own loop retries inside the operation budget.</summary>
    public bool IsTransientIncompleteRead =>
        (Code == ErrorCode.Timeout || Code == ErrorCode.AppUnresponsive) && IsExplicitlyRetryable;

    public bool IsRetryableResolutionFailure =>
        (Code == ErrorCode.StaleRef
            || Code == ErrorCode.AmbiguousTarget
            || Code == ErrorCode.Timeout
            || Code == ErrorCode.AppUnresponsive)
        && IsExplicitlyRetryable;

    /// <summary>The trait-default answer a defaulted capability method returns for a method.</summary>
    public bool IsDefaultNotSupported(string method) =>
        Code == ErrorCode.PlatformNotSupported
        && Message == $"{method} is not supported on this platform";

    /// <summary>An error for a documented code with no delivery evidence, so the disposition stays unknown.</summary>
    public static AdapterError FromCode(ErrorCode code, string message) => new(code, message);

    public static AdapterError NotSupported(string method) =>
        new AdapterError(ErrorCode.PlatformNotSupported, $"{method} is not supported on this platform")
            .WithSuggestion("Use a platform/build that advertises this capability or choose a supported command")
            .WithDisposition(DeliverySemantics.NotDelivered());

    public static AdapterError Stale(string refId) =>
        new AdapterError(ErrorCode.StaleRef, $"{refId} not found in current RefMap")
            .WithSuggestion(
                "Re-run a snapshot to obtain fresh refs, then retry with the new ref "
                + "(CLI: snapshot [--skeleton]; FFI: ad_snapshot then pass the printed qualified ref to ad_execute_by_ref)")
            .WithDisposition(DeliverySemantics.NotDelivered());

    public static AdapterError StaleBecause(string message) =>
        new AdapterError(ErrorCode.StaleRef, message)
            .WithSuggestion("Run 'snapshot' to refresh, then retry with the updated ref.")
            .WithDisposition(DeliverySemantics.NotDelivered());

    public static AdapterError AmbiguousTarget(string message) =>
        new AdapterError(ErrorCode.AmbiguousTarget, message)
            .WithSuggestion("Re-run a snapshot to refresh refs, then retry with a more specific ref")
            .WithDisposition(DeliverySemantics.NotDelivered());

    public static AdapterError ElementNotFound(string refId) =>
        new AdapterError(ErrorCode.ElementNotFound, $"Element {refId} could not be resolved")
            .WithSuggestion("Re-run a snapshot to obtain fresh refs, then retry with the new ref")
            .WithDisposition(DeliverySemantics.NotDelivered());

    public static AdapterError AppHasNoWindow(string app) =>
        new AdapterError(ErrorCode.WindowNotFound, $"Application '{app}' is running but has no matching window")
            .WithSuggestion("Wait for the app to present a window, or run 'list-windows --app'.");

    public static AdapterError Timeout(string message) =>
        new AdapterError(ErrorCode.Timeout, message)
            .WithSuggestion("The target application may be busy or unresponsive");

    public static AdapterError InvalidArgs(string message) =>
        new AdapterError(ErrorCode.InvalidArgs, message).Unsent();

    public static AdapterError IncompleteLiveEvidence() =>
        new AdapterError(ErrorCode.AppUnresponsive, "Live element evidence was incomplete")
            .WithDetails("""
                {"kind":"live_element_evidence","complete":false,"retryable":true}
                """);
}
