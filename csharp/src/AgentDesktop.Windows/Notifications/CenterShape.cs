namespace AgentDesktop.Windows.Notifications;

/// <summary>
/// What the open surface's landmark set says about its content. An open center carries either the notification list
/// (A26-3, notifications present) or the empty-center landmarks in its place (A26-3, measured as the empty state's
/// shape on this build). It carries neither only when the shell's tree does not match the shape this adapter reads,
/// which is a refusal, never an empty answer.
/// </summary>
internal enum CenterShape
{
    WalkList,
    EmptyCenter,
}
