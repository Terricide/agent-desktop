namespace AgentDesktop.Cli;

/// <summary>The permission a command needs before it reaches the application.</summary>
public enum PermissionNeed
{
    None,
    Accessibility,
    ScreenRecording,
    AccessibilityAndScreenRecording,
}

/// <summary>The command surface: every name the CLI accepts, and the permission each one
/// needs. A name outside the table is refused before the adapter is reached, and a command
/// the platform does not implement fails closed with PLATFORM_NOT_SUPPORTED.</summary>
public static class CommandTable
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>
    {
        "snapshot", "find", "screenshot", "get", "is",
        "click", "double-click", "triple-click", "right-click", "type", "set-value",
        "clear", "focus", "select", "toggle", "check", "uncheck", "expand", "collapse",
        "scroll", "scroll-to",
        "press", "key-down", "key-up", "hover", "drag", "mouse-move", "mouse-click",
        "mouse-down", "mouse-up", "mouse-wheel",
        "launch", "close-app", "focus-window", "resize-window", "move-window", "minimize",
        "maximize", "restore", "open-system-surface",
        "list-apps", "list-windows", "list-displays", "list-surfaces",
        "list-notifications", "dismiss-notification", "dismiss-all-notifications",
        "notification-action",
        "clipboard-get", "clipboard-set", "clipboard-clear",
        "wait", "status", "permissions", "version", "batch", "skills", "session",
        "cursor-overlay", "trace",
    };

    /// <summary>The permission a command needs. Reading a surface and acting on a ref need
    /// accessibility; a whole-screen capture needs screen recording.</summary>
    public static PermissionNeed PolicyFor(string name) => name switch
    {
        "version" or "skills" or "session" or "cursor-overlay" or "trace" or "status"
            or "permissions" or "list-windows" or "list-displays" or "list-apps"
            or "clipboard-get" or "clipboard-set" or "clipboard-clear" or "batch" =>
            PermissionNeed.None,
        "screenshot" => PermissionNeed.ScreenRecording,
        _ => PermissionNeed.Accessibility,
    };

    /// <summary>Whether a command changes application state. A mutating command under a
    /// multi-agent lease needs the agent identified.</summary>
    public static bool IsMutating(string name) =>
        Names.Contains(name) && PolicyFor(name) == PermissionNeed.Accessibility && name != "snapshot";
}
