using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Snapshot;

/// <summary>The surface a snapshot roots: a window, a menu, a shell surface, a desktop element.</summary>
public enum SnapshotSurface
{
    Window,
    Focused,
    Menu,
    Menubar,
    Sheet,
    Popover,
    Alert,
    Desktop,
    Taskbar,
    SystemTray,
    QuickSettings,
    NotificationCenter,
    Toolbar,
    Dock,
    Spotlight,
    MenuBarExtras,
    SystemTrayOverflow,
    StartMenu,
    ActionCenter,
}

public static class SnapshotSurfaceNames
{
    public static string AsString(this SnapshotSurface surface) => surface switch
    {
        SnapshotSurface.Window => "window",
        SnapshotSurface.Focused => "focused",
        SnapshotSurface.Menu => "menu",
        SnapshotSurface.Menubar => "menubar",
        SnapshotSurface.Sheet => "sheet",
        SnapshotSurface.Popover => "popover",
        SnapshotSurface.Alert => "alert",
        SnapshotSurface.Desktop => "desktop",
        SnapshotSurface.Taskbar => "taskbar",
        SnapshotSurface.SystemTray => "system_tray",
        SnapshotSurface.QuickSettings => "quick_settings",
        SnapshotSurface.NotificationCenter => "notification_center",
        SnapshotSurface.Toolbar => "toolbar",
        SnapshotSurface.Dock => "dock",
        SnapshotSurface.Spotlight => "spotlight",
        SnapshotSurface.MenuBarExtras => "menu_bar_extras",
        SnapshotSurface.SystemTrayOverflow => "system_tray_overflow",
        SnapshotSurface.StartMenu => "start_menu",
        SnapshotSurface.ActionCenter => "action_center",
        _ => "window",
    };

    /// <summary>Accepts the snake_case name and the kebab-case CLI alias for the same surface.</summary>
    public static SnapshotSurface? Parse(string value) => value switch
    {
        "window" => SnapshotSurface.Window,
        "focused" => SnapshotSurface.Focused,
        "menu" => SnapshotSurface.Menu,
        "menubar" => SnapshotSurface.Menubar,
        "sheet" => SnapshotSurface.Sheet,
        "popover" => SnapshotSurface.Popover,
        "alert" => SnapshotSurface.Alert,
        "desktop" => SnapshotSurface.Desktop,
        "taskbar" => SnapshotSurface.Taskbar,
        "system_tray" or "system-tray" => SnapshotSurface.SystemTray,
        "quick_settings" or "quick-settings" => SnapshotSurface.QuickSettings,
        "notification_center" or "notification-center" => SnapshotSurface.NotificationCenter,
        "toolbar" => SnapshotSurface.Toolbar,
        "dock" => SnapshotSurface.Dock,
        "spotlight" => SnapshotSurface.Spotlight,
        "menu_bar_extras" or "menu-bar-extras" => SnapshotSurface.MenuBarExtras,
        "system_tray_overflow" or "system-tray-overflow" => SnapshotSurface.SystemTrayOverflow,
        "start_menu" or "start-menu" => SnapshotSurface.StartMenu,
        "action_center" or "action-center" => SnapshotSurface.ActionCenter,
        _ => null,
    };

    public static bool IsWindow(SnapshotSurface surface) => surface == SnapshotSurface.Window;

    /// <summary>The refusal for a surface an adapter does not root: a wrong argument when the platform roots other kinds, a platform limit when it roots none.</summary>
    public static AdapterError Unavailable(this SnapshotSurface requested, SnapshotSurface[] supported)
    {
        var code = supported.Length == 0 ? ErrorCode.PlatformNotSupported : ErrorCode.InvalidArgs;
        var supportedNames = new List<string>();
        foreach (var surface in supported)
        {
            supportedNames.Add(surface.AsString());
        }

        var supportedArray = new JsonArray();
        foreach (var name in supportedNames)
        {
            supportedArray.Add((JsonNode)name);
        }

        var details = new JsonObject();
        details["requested_surface"] = requested.AsString();
        details["supported_surfaces"] = supportedArray;

        return new AdapterError(
            code,
            $"Snapshot surface '{requested.AsString()}' is not available on this platform")
            .WithDetails(details.ToJsonString())
            .WithSuggestion("Choose one of the supported snapshot surfaces")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }
}
