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
    /// <summary>Reads the retryable stamp from details JSON text; absent means unspecified. The
    /// stamp is a documented field, so a text scan is enough and the envelope path never parses
    /// a document.</summary>
    public static Retryability FromDetails(string details)
    {
        var index = details.IndexOf("\"retryable\"", StringComparison.Ordinal);
        if (index < 0)
        {
            return Retryability.Unspecified;
        }

        var colon = details.IndexOf(':', index + 11);
        if (colon < 0)
        {
            return Retryability.Unspecified;
        }

        var rest = details.AsSpan(colon + 1).TrimStart();
        if (rest.StartsWith("true", StringComparison.Ordinal))
        {
            return Retryability.Retryable;
        }

        return rest.StartsWith("false", StringComparison.Ordinal) ? Retryability.NonRetryable : Retryability.Unspecified;
    }
}
