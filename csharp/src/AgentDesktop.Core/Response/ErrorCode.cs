namespace AgentDesktop.Core.Response;

/// <summary>Machine-readable codes the JSON envelope carries in error.code.</summary>
public enum ErrorCode
{
    PermDenied,
    ElementNotFound,
    AppNotFound,
    ActionFailed,
    ActionNotSupported,
    StaleRef,
    AmbiguousTarget,
    WindowNotFound,
    PlatformNotSupported,
    Timeout,
    InvalidArgs,
    NotificationNotFound,
    SnapshotNotFound,
    PolicyDenied,
    AppUnresponsive,
    Internal,
}

public static class ErrorCodeNames
{
    /// <summary>The SCREAMING_SNAKE_CASE spelling the envelope serializes.</summary>
    public static string AsString(this ErrorCode code) => code switch
    {
        ErrorCode.PermDenied => "PERM_DENIED",
        ErrorCode.ElementNotFound => "ELEMENT_NOT_FOUND",
        ErrorCode.AppNotFound => "APP_NOT_FOUND",
        ErrorCode.ActionFailed => "ACTION_FAILED",
        ErrorCode.ActionNotSupported => "ACTION_NOT_SUPPORTED",
        ErrorCode.StaleRef => "STALE_REF",
        ErrorCode.AmbiguousTarget => "AMBIGUOUS_TARGET",
        ErrorCode.WindowNotFound => "WINDOW_NOT_FOUND",
        ErrorCode.PlatformNotSupported => "PLATFORM_NOT_SUPPORTED",
        ErrorCode.Timeout => "TIMEOUT",
        ErrorCode.InvalidArgs => "INVALID_ARGS",
        ErrorCode.NotificationNotFound => "NOTIFICATION_NOT_FOUND",
        ErrorCode.SnapshotNotFound => "SNAPSHOT_NOT_FOUND",
        ErrorCode.PolicyDenied => "POLICY_DENIED",
        ErrorCode.AppUnresponsive => "APP_UNRESPONSIVE",
        ErrorCode.Internal => "INTERNAL",
        _ => "INTERNAL",
    };
}
