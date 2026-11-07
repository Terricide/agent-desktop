using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Systems;

/// <summary>A window the platform can read, with the app that owns it.</summary>
public sealed record WindowInfo
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string App { get; init; } = "";
    public ProcessId Pid { get; init; }
    public string? ProcessInstance { get; init; }
    public Rect? Bounds { get; init; }
    public WindowState State { get; init; } = new();

    /// <summary>Names an app's process instead of one of its windows, for an app observed from its application root because it has no window.</summary>
    public static WindowInfo ForProcess(AppInfo app) => new()
    {
        Id = "",
        Title = "",
        App = app.Name,
        Pid = app.Pid,
        ProcessInstance = app.ProcessInstance,
        Bounds = null,
        State = new WindowState(),
    };

    public bool IsProcessOwner() => Id.Length == 0;
}
