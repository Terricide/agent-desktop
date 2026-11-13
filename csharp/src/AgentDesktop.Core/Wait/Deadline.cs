using System.Diagnostics;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Wait;

/// <summary>An operation budget. A deadline created inside a command scope is capped by the parent's expiry, so nested work cannot extend the caller's budget.</summary>
public readonly record struct Deadline(long StartedAt, long ExpiresAt, long TimeoutMs, bool Capped)
{
    public const long DefaultOperationTimeoutMs = 5_000;

    /// <summary>The inherited scope, held outside this struct. A [ThreadStatic] field of
    /// the containing record struct fails to load on the CoreCLR of .NET 8 and 9, which
    /// takes down every JIT-hosted process that builds a deadline; Native AOT is
    /// unaffected. A nested holder loads on all three runtimes.</summary>
    private static class Scope
    {
        [ThreadStatic]
        internal static Deadline? Inherited;
    }

    public static long Now() => Stopwatch.GetTimestamp();

    public static long ExpiryFor(long timeoutMs) =>
        Stopwatch.GetTimestamp() + (long)(timeoutMs * (Stopwatch.Frequency / 1000.0));

    /// <summary>A budget that is intentionally independent of the current command scope. Use only for recovery work that must run after an operation deadline expires.</summary>
    public static Deadline DetachedAfter(long timeoutMs)
    {
        if (timeoutMs < 0)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "Timeout cannot be represented safely")
                .WithDetails($"{{\"timeout_ms\":{timeoutMs}}}");
        }

        var started = Stopwatch.GetTimestamp();
        return new Deadline(started, started + (long)(timeoutMs * (Stopwatch.Frequency / 1000.0)), timeoutMs, false);
    }

    /// <summary>A budget for a new operation inside the current scope, capped by the scope's own expiry.</summary>
    public static Deadline After(long timeoutMs)
    {
        var detached = DetachedAfter(timeoutMs);
        var inherited = Scope.Inherited;
        if (inherited is not null && inherited.Value.ExpiresAt < detached.ExpiresAt)
        {
            return inherited.Value with { TimeoutMs = detached.TimeoutMs, Capped = true };
        }

        return detached;
    }

    public static Deadline Standard() => After(DefaultOperationTimeoutMs);

    /// <summary>Enters a scope for this deadline and returns the handle that restores the previous one.</summary>
    public ScopeHandle Enter()
    {
        var previous = Scope.Inherited;
        Scope.Inherited = this;
        return new ScopeHandle(previous);
    }

    /// <summary>A copy of this budget whose expiry is no later than <paramref name="maximum"/> from now. The
    /// inherited expiry still bounds the result, so a cap never extends the budget.</summary>
    public Deadline CappedBy(TimeSpan maximum)
    {
        var now = Stopwatch.GetTimestamp();
        var offset = maximum.TotalMilliseconds * (Stopwatch.Frequency / 1000.0);
        var expiresAt = offset > long.MaxValue - (double)now
            ? ExpiresAt
            : Math.Min(ExpiresAt, now + (long)offset);
        return this with { ExpiresAt = expiresAt, Capped = Capped || expiresAt < ExpiresAt };
    }

    public bool IsExpired() => Stopwatch.GetTimestamp() >= ExpiresAt;

    /// <summary>Wall-clock time spent since this budget started, which the batch reports as
    /// its elapsed time.</summary>
    public long ElapsedMs() => (Stopwatch.GetTimestamp() - StartedAt) * 1000 / Stopwatch.Frequency;

    public TimeSpan Remaining() => TimeSpan.FromMilliseconds(RemainingMs());

    public long RemainingMs()
    {
        var remaining = (ExpiresAt - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency;
        return remaining > 0 ? remaining : 0;
    }

    /// <summary>Restores the deadline a nested scope inherited from.</summary>
    public sealed class ScopeHandle : IDisposable
    {
        private Deadline? _previous;

        internal ScopeHandle(Deadline? previous) => _previous = previous;

        public void Dispose()
        {
            Scope.Inherited = _previous;
            _previous = null;
        }
    }
}
