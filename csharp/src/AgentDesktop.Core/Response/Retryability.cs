using System.Text.Json.Nodes;

namespace AgentDesktop.Core.Response;

/// <summary>What an error's details stamp says about repeating the same call.</summary>
public enum Retryability
{
    Unspecified,
    Retryable,
    NonRetryable,
}

public static class RetryabilityReader
{
    /// <summary>Reads the retryable stamp from a details object; absent means unspecified.</summary>
    public static Retryability FromDetails(JsonNode details)
    {
        if (details is not JsonObject obj)
        {
            return Retryability.Unspecified;
        }

        if (!obj.TryGetPropertyValue("retryable", out var stamp))
        {
            return Retryability.Unspecified;
        }

        if (stamp is JsonValue value)
        {
            if (value.TryGetValue(out bool flag))
            {
                return flag ? Retryability.Retryable : Retryability.NonRetryable;
            }
        }

        return Retryability.Unspecified;
    }
}
