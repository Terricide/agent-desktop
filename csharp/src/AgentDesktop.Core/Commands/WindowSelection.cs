using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Commands;

/// <summary>Picks the window a window-scoped command acts on, from the windows the platform listed.
/// A selector names it: an id, a title substring, or the app whose windows are the candidates. With no
/// selector the command refuses before the application is read, and a tie is refused with the candidates
/// so the caller can name one. Port of <c>select_window_for_target</c> and its callers in
/// <c>crates/core/src/commands/window_target.rs</c>.</summary>
public static class WindowSelection
{
    /// <summary>Resolves the selector to one window, or to the refusal the command should return. The
    /// tie message names the operation, so a resize that found two candidates says what it was trying
    /// to resize.</summary>
    public static (WindowInfo? Window, EnvelopeResponse? Refusal) Pick(
        PlatformAdapter adapter,
        string command,
        string? windowId,
        string? app,
        string? title,
        string verb)
    {
        var windows = adapter.ListWindows(new WindowFilter { App = app }, Deadline.Standard());

        List<WindowInfo> candidates;
        if (windowId is not null)
        {
            candidates = windows.Where(w => w.Id == windowId).ToList();
            if (candidates.Count == 0)
            {
                return (null, Envelope.Err(command, new AdapterError(
                        ErrorCode.WindowNotFound, $"No window with id '{windowId}'")
                    .WithSuggestion("Run 'list-windows' to see available windows and their IDs.")
                    .Unsent()));
            }
        }
        else if (title is not null)
        {
            candidates = windows.Where(w => w.Title.Contains(title, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0)
            {
                return (null, Envelope.Err(command, new AdapterError(
                        ErrorCode.WindowNotFound, $"No window with title containing '{title}'")
                    .WithSuggestion("Run 'list-windows' to see available windows and their IDs.")
                    .Unsent()));
            }
        }
        else if (app is not null)
        {
            candidates = windows;
            if (candidates.Count == 0)
            {
                return (null, Envelope.Err(command, new AdapterError(
                        ErrorCode.WindowNotFound, $"No windows found for app '{app}'")
                    .WithSuggestion("Run 'list-apps' to see available apps.")
                    .Unsent()));
            }
        }
        else
        {
            return (null, Envelope.Err(command,
                AdapterError.InvalidArgs("Provide --window-id, --app, or --title to identify the window")));
        }

        var window = WindowLookup.SelectWindow(
            candidates,
            new AdapterError(ErrorCode.WindowNotFound, "No matching window found"),
            $"More than one window matches the {verb} target");
        return (window, null);
    }
}
