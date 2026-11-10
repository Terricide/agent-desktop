namespace AgentDesktop.Core.Notifications;

/// <summary>The application and title a notification must carry to be the one an action targets. An unset or
/// empty field places no constraint.</summary>
public sealed class NotificationIdentity
{
    public string? ExpectedApp { get; init; }

    public string? ExpectedTitle { get; init; }

    public bool IsEmpty() =>
        string.IsNullOrEmpty(ExpectedApp) && string.IsNullOrEmpty(ExpectedTitle);

    public bool Matches(NotificationInfo info) =>
        (ExpectedApp is null || string.Equals(ExpectedApp, info.AppName, StringComparison.Ordinal))
        && (ExpectedTitle is null || string.Equals(ExpectedTitle, info.Title, StringComparison.Ordinal));
}
