using AgentDesktop.Core.Notifications;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Notifications;

/// <summary>
/// Verifies notification mutations by re-reading the notification center. Judgements use the identity of
/// the captured entries, never their position or mutable control values.
/// </summary>
internal static class NotificationVerify
{
    static readonly TimeSpan ReobservationInterval = TimeSpan.FromMilliseconds(25);
    static readonly TimeSpan RemovalSettleTime = TimeSpan.FromMilliseconds(2_000);

    /// <summary>
    /// The identity a mutation verified against: source application, title and body. Mutable control values
    /// never participate; an entry whose toggle state or progress changed is still the entry that was targeted.
    /// </summary>
    internal static bool SameIdentity(NotificationInfo original, NotificationInfo current)
    {
        return string.Equals(original.AppName, current.AppName, StringComparison.Ordinal)
            && string.Equals(original.Title, current.Title, StringComparison.Ordinal)
            && string.Equals(original.Body, current.Body, StringComparison.Ordinal);
    }

    internal static int MatchingCount(IReadOnlyList<NotificationInfo> entries, NotificationInfo target)
    {
        var count = 0;
        foreach (var current in entries)
        {
            if (SameIdentity(target, current))
            {
                count++;
            }
        }
        return count;
    }

    internal static bool EntryGone(NotificationInfo target, IReadOnlyList<NotificationInfo> current)
    {
        return MatchingCount(current, target) == 0;
    }

    /// <summary>
    /// The captured set's members an ignored clear would leave behind.
    /// </summary>
    /// <remarks>
    /// Only membership in the set captured before the clear counts as a failure: entries outside it arrived
    /// after the clear was invoked and are new arrivals, not survivors. This is the distinction an emptiness
    /// check cannot make. A re-read showing N entries is either an ignored clear or a cleared list with N
    /// re-posted entries, and only the captured set tells them apart.
    /// </remarks>
    internal static List<string> SurvivorFailures(
        IReadOnlyList<NotificationInfo> captured,
        IReadOnlyList<NotificationInfo> current)
    {
        var failures = new List<string>();
        foreach (var member in captured)
        {
            if (!EntryGone(member, current))
            {
                failures.Add($"#{member.Index}: notification from the captured set is still present");
            }
        }
        return failures;
    }

    /// <summary>
    /// Whether the entry an action targeted is gone or no longer carries the identity it was read with.
    /// </summary>
    internal static bool ActionChangedState(NotificationInfo original, IReadOnlyList<NotificationInfo> current)
    {
        return EntryGone(original, current);
    }

    internal static AdapterError DismissSurvivedError(int index)
    {
        return new AdapterError(
                ErrorCode.ActionFailed,
                $"The dismiss of the notification at index {index} was invoked but the entry is still present")
            .WithSuggestion("Retry the dismissal: the shell can accept an invoke without acting on it")
            .WithDisposition(DeliverySemantics.DeliveredUnverified());
    }

    /// <summary>
    /// Re-reads the center until every target has vanished or the settle window closes, and returns the last
    /// listing either way.
    /// </summary>
    /// <remarks>
    /// The shell accepts an invoke and applies it a moment later, so the read that judges a mutation is a polled
    /// read, not the first one. An expired settle is an answer (the targets survived the window), never an
    /// error; the caller decides what survival means.
    /// </remarks>
    internal static IReadOnlyList<NotificationInfo> ReadSettlingWithout(
        IReadOnlyList<NotificationInfo> targets,
        nint hwnd,
        NotificationFilter filter,
        Deadline deadline)
    {
        var settle = deadline.CappedBy(RemovalSettleTime);
        var current = NotificationList.ListInfos(filter, hwnd, settle);
        while (!targets.All(target => EntryGone(target, current)))
        {
            if (settle.IsExpired())
            {
                return current;
            }
            Thread.Sleep(settle.RemainingSlice(ReobservationInterval));
            try
            {
                current = NotificationList.ListInfos(filter, hwnd, settle);
            }
            catch (AdapterError error) when (error.Code == ErrorCode.Timeout)
            {
                return current;
            }
        }
        return current;
    }
}
