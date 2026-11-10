using System.Diagnostics;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree;

/// <summary>How long the resolution retry loop waits between attempts. A fixed pause spends
/// the whole budget at one rate, and a provider that is genuinely struggling would be asked
/// forty times a second for as long as the deadline allows. Doubling keeps the fast case fast
/// and lets the hopeless case cost little; the ceiling keeps a long deadline from stretching
/// one pause past usefulness.</summary>
internal static class ResolvePacing
{
    internal static readonly TimeSpan FirstRetryPause = TimeSpan.FromMilliseconds(25);

    internal static readonly TimeSpan MaximumRetryPause = TimeSpan.FromMilliseconds(400);

    /// <summary>Sleeps <paramref name="pause"/>, or whatever is left of the deadline if that is shorter.</summary>
    internal static void SleepBeforeRetry(Deadline deadline, TimeSpan pause)
    {
        var remaining = RemainingTime(deadline);
        Thread.Sleep(remaining < pause ? remaining : pause);
    }

    /// <summary>The pause the next retry should use after sleeping <paramref name="pause"/>.</summary>
    internal static TimeSpan NextRetryPause(TimeSpan pause)
    {
        var doubled = pause + pause;
        return doubled < MaximumRetryPause ? doubled : MaximumRetryPause;
    }

    private static TimeSpan RemainingTime(Deadline deadline)
    {
        var ticks = deadline.ExpiresAt - Stopwatch.GetTimestamp();
        return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(ticks / (double)Stopwatch.Frequency);
    }
}
