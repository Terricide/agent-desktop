using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Refs;

/// <summary>The resolver errors core owns, so every platform stamps the same payload.</summary>
public static class ResolveErrors
{
    /// <summary>The incomplete-and-retryable answer for a candidate whose identity could not be read from the live
    /// accessibility evidence. The payload is decided once here, so no platform copy can drift from it.</summary>
    public static AdapterError IdentityUnknownError(RefEntry entry) =>
        new AdapterError(
                ErrorCode.Timeout,
                "Strict resolution could not determine candidate identity from the live accessibility evidence")
            .WithSuggestion("Retry after the target application finishes updating its accessibility tree")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithDetails(new JsonObject
            {
                ["kind"] = "resolution_identity_unknown",
                ["role"] = entry.Identity.Role,
                ["complete"] = false,
                ["retryable"] = true,
            });
}
