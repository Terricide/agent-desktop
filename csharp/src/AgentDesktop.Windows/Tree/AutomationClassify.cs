using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Tree;

/// <summary>HRESULT and client-sentinel classification for UI Automation failures. Turns one failure into a
/// structured AdapterError carrying the complete/retryable disposition that the resolution retry loop and core
/// hydration read from the details payload. A settled absence or terminal failure is complete and never retried;
/// a transport failure or a vanished element is incomplete and retryable. The disposition and the ErrorCode trace
/// back to the same record, so they cannot come from different facts about one code. The context phrase is
/// shape-only: no observed target text may reach it, because adapter error text is cloned into session traces.</summary>
internal static class AutomationClassify
{
    internal const int ErrNone = 0;

    internal const int ErrNotFound = 1;

    internal const int ErrTimeout = 2;

    internal const int ErrInactive = 3;

    internal const int ErrType = 4;

    internal const int ErrNullPtr = 5;

    internal const int ErrFormat = 6;

    internal const int ErrInvalidObject = 7;

    internal const int ErrAlreadyRunning = 8;

    internal const int ErrInvalidArg = 9;

    /// <summary>The read-path disposition of one failure. Mirrors ReadDisposition in
    /// crates/windows/src/system/runtime/hresult.rs; the retry details are derived from it.</summary>
    internal enum ReadDisposition
    {
        SettledAbsence,
        Terminal,
        Retryable,
        Unavailable,
    }

    /// <summary>One UI Automation failure, split on its discriminator. The uiautomation client reuses small
    /// non-negative sentinels in the same field as a real HRESULT, so the value alone is ambiguous.</summary>
    internal readonly record struct Failure(bool IsHresult, int Value)
    {
        internal static Failure Hresult(int hresult) => new(true, hresult);

        internal static Failure Sentinel(int sentinel) => new(false, sentinel);

        /// <summary>The benign end-of-list signal a child enumeration must not mistake for a fault.</summary>
        internal bool IsExhaustion => !IsHresult && Value == ErrNone;

        /// <summary>The element or window no longer exists at the addressed handle.</summary>
        internal bool IsMissingTarget =>
            IsHresult ? Value == UiaFailure.ElementNotAvailable : Value == ErrNotFound;

        /// <summary>Whether a read whose result is optional should answer "no match" rather than surface the
        /// failure. Any other failure means the search itself could not run and must not be dressed as absence.</summary>
        internal bool IsAbsence => IsExhaustion || IsMissingTarget;
    }

    /// <summary>Builds a structured adapter error from one UI Automation failure. The message names the context;
    /// the platform detail carries the HRESULT or the client status, never target text.</summary>
    internal static AdapterError UiaFailureError(Failure failure, string context)
    {
        var (complete, retryable) = RetryDetails(DispositionOf(failure));
        var details = new JsonObject { ["complete"] = complete, ["retryable"] = retryable };
        var message = $"UI Automation could not {context}";
        if (failure.IsHresult)
        {
            var hresult = failure.Value;
            var (code, _) = UiaFailure.Classify(hresult);
            var error = new AdapterError(code, message)
                .WithPlatformDetail(UiaFailure.Detail(hresult))
                .WithDetails(details);
            return unchecked((uint)hresult) == ComNotInitialized
                ? error.WithSuggestion("Join the calling thread to the COM multithreaded apartment before observing the desktop")
                : error;
        }

        return new AdapterError(SentinelRecord(failure.Value).Code, message)
            .WithPlatformDetail($"UI Automation client status {failure.Value}")
            .WithDetails(details);
    }

    /// <summary>Rewrites a root-resolution failure so a window that no longer exists reads as a missing window.
    /// The complete/retryable pair is re-stamped rather than inherited: at window-root granularity a destroyed
    /// window never answers the same handle again, so it is a settled absence, and the suggestion asks for a new
    /// handle rather than a repeat of the call.</summary>
    internal static AdapterError RootResolutionError(Failure failure)
    {
        var error = UiaFailureError(failure, "resolve a window root");
        if (!failure.IsMissingTarget)
        {
            return error;
        }

        var (complete, retryable) = RetryDetails(ReadDisposition.SettledAbsence);
        return new AdapterError(ErrorCode.WindowNotFound, "UI Automation could not resolve a window root")
            .WithPlatformDetail(error.PlatformDetail ?? string.Empty)
            .WithSuggestion("List windows again and retry with a current window handle")
            .WithDetails(new JsonObject
            {
                ["kind"] = "window_root_missing",
                ["complete"] = complete,
                ["retryable"] = retryable,
            });
    }

    /// <summary>The read-path disposition of a failure across both branches.</summary>
    internal static ReadDisposition DispositionOf(Failure failure) =>
        failure.IsHresult
            ? (UiaFailure.Classify(failure.Value).Retryable ? ReadDisposition.Retryable : ReadDisposition.Terminal)
            : SentinelRecord(failure.Value).Disposition;

    /// <summary>The complete and retryable stamp for a disposition. Settled absence and terminal failure are
    /// complete and never retried; transport failure and a vanished element are incomplete and retryable.</summary>
    internal static (bool Complete, bool Retryable) RetryDetails(ReadDisposition disposition) => disposition switch
    {
        ReadDisposition.SettledAbsence or ReadDisposition.Terminal => (true, false),
        ReadDisposition.Retryable or ReadDisposition.Unavailable => (false, true),
        _ => (true, false),
    };

    private const uint ComNotInitialized = 0x8004_01F0;

    /// <summary>Names every client sentinel this read path raises. An unlisted sentinel is Internal and terminal.</summary>
    private static (ErrorCode Code, ReadDisposition Disposition) SentinelRecord(int sentinel) => sentinel switch
    {
        ErrNotFound => (ErrorCode.ElementNotFound, ReadDisposition.Unavailable),
        ErrNullPtr => (ErrorCode.ElementNotFound, ReadDisposition.Terminal),
        ErrTimeout => (ErrorCode.Timeout, ReadDisposition.Retryable),
        ErrInactive => (ErrorCode.AppUnresponsive, ReadDisposition.Retryable),
        ErrInvalidObject => (ErrorCode.StaleRef, ReadDisposition.Unavailable),
        ErrInvalidArg => (ErrorCode.InvalidArgs, ReadDisposition.SettledAbsence),
        ErrNone or ErrType or ErrFormat or ErrAlreadyRunning => (ErrorCode.Internal, ReadDisposition.Terminal),
        _ => (ErrorCode.Internal, ReadDisposition.Terminal),
    };
}
