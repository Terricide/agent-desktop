using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Notifications;

/// <summary>
/// Confirms the notification read at <c>index</c> is still the one the caller observed. An absent or
/// empty identity accepts any entry; otherwise the expected app and title must match, and a mismatch
/// means the list moved. Port of
/// <c>crates/core/src/notifications/notification_identity_check.rs</c>.
/// </summary>
public static class NotificationIdentityCheck
{
    public static void Verify(int index, NotificationIdentity? identity, NotificationInfo info)
    {
        if (identity is null || identity.IsEmpty() || identity.Matches(info))
        {
            return;
        }

        throw new AdapterError(
                ErrorCode.NotificationNotFound,
                $"Notification at index {index} no longer matches its expected identity")
            .WithSuggestion("Run list-notifications again and retry with the freshly-observed index");
    }
}
