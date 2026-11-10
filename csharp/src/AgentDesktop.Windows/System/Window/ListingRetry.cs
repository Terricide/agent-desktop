using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Window;

/// <summary>
/// Retry helpers for the live window listing. The race budget is shared by every caller on this path
/// (the signal inventory's single-pass walk, the launch command's window observation, and the window
/// listing itself) so the budget cannot silently drift between them and leave one caller retrying less
/// than the others.
/// </summary>
internal static class ListingRetry
{
    /// <summary>
    /// How many times a walk over the live window set is re-attempted after it catches its own transient
    /// identity race, before that race is accepted as a genuine refusal.
    /// </summary>
    internal const int ListingRaceAttempts = 5;

    /// <summary>
    /// Retries <paramref name="attempt"/> up to <see cref="ListingRaceAttempts"/> times under
    /// <paramref name="deadline"/>, continuing only while <paramref name="isRace"/> classifies the thrown
    /// error as the transient mid-walk identity race this helper exists to absorb.
    /// </summary>
    /// <remarks>
    /// Any other error propagates immediately, and so does the last race error once every attempt is
    /// spent: what a caller does with an exhausted race (a synthesized error, a null result, the raw
    /// refusal unchanged) is its own decision, not this helper's. Each attempt opens with the same
    /// <c>EnsureRemaining</c> preamble every native call on this path uses, so a deadline that expires
    /// mid-retry surfaces as <c>TIMEOUT</c> rather than as one more race attempt.
    /// </remarks>
    internal static T RetryTransientWindowRace<T>(
        Deadline deadline,
        global::System.Func<AdapterError, bool> isRace,
        global::System.Func<T> attempt)
    {
        AdapterError? lastRaceError = null;
        for (var index = 0; index < ListingRaceAttempts; index++)
        {
            deadline.EnsureRemaining();
            try
            {
                return attempt();
            }
            catch (AdapterError error) when (isRace(error))
            {
                lastRaceError = error;
            }
        }

        throw lastRaceError ?? deadline.TimeoutError();
    }

    /// <summary>
    /// Narrows any failure raised on this feature set's read paths onto the closed set the core poll loop
    /// retries: <c>TIMEOUT</c> passes through unchanged, everything else becomes <c>APP_UNRESPONSIVE</c>,
    /// the one "could not read the desktop right now" code every caller on this path accepts.
    /// </summary>
    /// <remarks>
    /// The code is get-only on <see cref="AdapterError"/>, so the narrowed error is rebuilt with the same
    /// message, suggestion, platform detail, details, disposition and retryability stamp.
    /// </remarks>
    internal static AdapterError NarrowToPermittedCodes(AdapterError error)
    {
        if (error.Code == ErrorCode.Timeout)
        {
            return error;
        }

        var narrowed = new AdapterError(ErrorCode.AppUnresponsive, error.Message)
            .WithDisposition(error.Disposition);
        if (error.Suggestion is not null)
        {
            narrowed.WithSuggestion(error.Suggestion);
        }

        if (error.PlatformDetail is not null)
        {
            narrowed.WithPlatformDetail(error.PlatformDetail);
        }

        if (error.Details is not null)
        {
            narrowed.WithDetails(error.Details);
        }

        return narrowed;
    }
}
