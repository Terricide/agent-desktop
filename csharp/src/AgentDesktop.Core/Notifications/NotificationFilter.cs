namespace AgentDesktop.Core.Notifications;

/// <summary>Narrows a notification listing by application name, by text, and by a maximum count.</summary>
public sealed class NotificationFilter
{
    public string? App { get; set; }

    public string? Text { get; set; }

    public int? Limit { get; set; }
}
