using System;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Wait;

/// <summary>Deadline checks that fail with the shared timeout error once the budget is spent.</summary>
public static class DeadlineChecks
{
    public static void EnsureRemaining(this Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw deadline.TimeoutError();
        }
    }

    public static AdapterError TimeoutError(this Deadline deadline) =>
        AdapterError.Timeout("Operation exceeded its deadline")
            .WithDetails($"{{\"kind\":\"deadline\",\"timeout_ms\":{deadline.TimeoutMs},\"elapsed_ms\":{deadline.ElapsedMs()}}}");

    /// <summary>The remaining budget cut at a maximum, mirroring deadline.rs remaining_slice. A
    /// budget already spent is the shared timeout error, so a caller cannot slice a slice of zero
    /// and keep polling.</summary>
    public static TimeSpan RemainingSlice(this Deadline deadline, TimeSpan maximum)
    {
        var remaining = deadline.RemainingMs();
        if (remaining <= 0)
        {
            throw deadline.TimeoutError();
        }

        return remaining < maximum.TotalMilliseconds ? TimeSpan.FromMilliseconds(remaining) : maximum;
    }
}
