namespace AgentDesktop.Core.Adapter;

/// <summary>The permission state an adapter read for each permission its delivery needs.</summary>
public sealed record PermissionReport
{
    public PermissionState Accessibility { get; init; }
    public PermissionState ScreenRecording { get; init; }
    public PermissionState Automation { get; init; }
}
