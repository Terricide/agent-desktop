namespace AgentDesktop.Core.Notifications;

/// <summary>One notification as read from the notification surface. The optional body and the action names
/// are omitted from the JSON when absent or empty.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial class NotificationInfo
{
    public int Index { get; set; }

    public string AppName { get; init; } = "";

    public string Title { get; init; } = "";

    public string? Body { get; init; }

    public List<string> Actions { get; init; } = new();
}
