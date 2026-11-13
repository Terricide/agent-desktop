using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Systems;

/// <summary>Writes the app and window inventory with keys in sorted order, the order the
/// envelope uses at every object level. A field appears only when it carries a value.</summary>
public static class InventoryJson
{
    /// <summary>The apps array: name, pid, and the identity the platform reported.</summary>
    public static void WriteApps(EnvelopeWriter w, List<AppInfo> apps)
    {
        w.ObjectStart();
        w.Key("apps");
        w.ArrayStart();
        foreach (var app in apps)
        {
            w.Member();
            App(w, app);
        }

        w.ArrayEnd();
        w.ObjectEnd();
    }

    /// <summary>The windows array: identity, geometry, and the flattened window state.</summary>
    public static void WriteWindows(EnvelopeWriter w, List<WindowInfo> windows)
    {
        w.ArrayStart();
        foreach (var window in windows)
        {
            w.Member();
            Window(w, window);
        }

        w.ArrayEnd();
    }

    private static void App(EnvelopeWriter w, AppInfo app)
    {
        w.ObjectStart();
        Text(w, "bundle_id", app.BundleId);
        Text(w, "name", app.Name);
        w.Key("pid");
        w.Number(app.Pid.Value);
        Text(w, "presentation", app.Presentation is null ? null : app.Presentation == AppPresentation.Foreground ? "foreground" : "background");
        Text(w, "process_instance", app.ProcessInstance);
        w.ObjectEnd();
    }

    /// <summary>The displays array: id, bounds, primary, and effective scale. The array is the whole
    /// data, the shape `list-displays` writes.</summary>
    public static void WriteDisplays(EnvelopeWriter w, List<DisplayInfo> displays)
    {
        w.ArrayStart();
        foreach (var display in displays)
        {
            w.Member();
            Display(w, display);
        }

        w.ArrayEnd();
    }

    private static void Display(EnvelopeWriter w, DisplayInfo display)
    {
        w.ObjectStart();
        Bounds(w, display.Bounds);
        w.Key("id");
        w.String(display.Id);
        w.Key("is_primary");
        w.Bool(display.IsPrimary);
        w.Key("scale");
        w.Number(display.Scale);
        w.ObjectEnd();
    }

    /// <summary>One window in the inventory shape, the shape 'focus-window' reports as the window it
    /// brought to the front.</summary>
    public static void WriteWindow(EnvelopeWriter w, WindowInfo window) => Window(w, window);

    private static void Window(EnvelopeWriter w, WindowInfo window)
    {
        w.ObjectStart();
        w.Key("accessible");
        w.Bool(window.State.Accessible);
        w.Key("app_name");
        w.String(window.App);
        Bounds(w, window.Bounds);
        w.Key("id");
        w.String(window.Id);
        w.Key("is_focused");
        w.Bool(window.State.IsFocused);
        if (window.State.Minimized is not null)
        {
            w.Key("minimized");
            w.Bool(window.State.Minimized.Value);
        }

        w.Key("pid");
        w.Number(window.Pid.Value);
        Text(w, "process_instance", window.ProcessInstance);
        w.Key("title");
        w.String(window.Title);
        if (window.State.Visible is not null)
        {
            w.Key("visible");
            w.Bool(window.State.Visible.Value);
        }

        w.ObjectEnd();
    }

    private static void Bounds(EnvelopeWriter w, Tree.Rect? bounds)
    {
        if (bounds is null)
        {
            return;
        }

        var rect = bounds.Value;
        w.Key("bounds");
        w.ObjectStart();
        w.Key("height");
        w.Number(rect.Height);
        w.Key("width");
        w.Number(rect.Width);
        w.Key("x");
        w.Number(rect.X);
        w.Key("y");
        w.Number(rect.Y);
        w.ObjectEnd();
    }

    private static void Text(EnvelopeWriter w, string name, string? value)
    {
        if (value is null)
        {
            return;
        }

        w.Key(name);
        w.String(value);
    }
}
