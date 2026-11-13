using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Commands;

/// <summary>Brings one window to the front. The window is chosen the way every window-scoped command
/// chooses one, from the windows the platform listed, narrowed by visibility, then focus, then
/// accessibility, and a tie is refused with the candidates. The adapter returns only once the target
/// window is confirmed focused, so a window the app keeps in the background fails with the activation
/// refusal rather than reporting a focus the window never took.</summary>
public sealed class FocusWindowCommand
{
    private readonly PlatformAdapter _adapter;

    public FocusWindowCommand(PlatformAdapter adapter) => _adapter = adapter;

    /// <summary>Runs one focus. A selector names the window: an id, a title substring, or the app whose
    /// windows are the candidates. With no selector the command refuses before the application is read.</summary>
    public EnvelopeResponse Run(string command, string? windowId, string? app, string? title)
    {
        var deadline = Deadline.Standard();
        var windows = _adapter.ListWindows(new WindowFilter { App = app }, deadline);

        List<WindowInfo> candidates;
        if (windowId is not null)
        {
            candidates = windows.Where(w => w.Id == windowId).ToList();
            if (candidates.Count == 0)
            {
                return Envelope.Err(command, new AdapterError(ErrorCode.WindowNotFound, $"No window with id '{windowId}'")
                    .WithSuggestion("Run 'list-windows' to see available windows and their IDs.").Unsent());
            }
        }
        else if (title is not null)
        {
            candidates = windows.Where(w => w.Title.Contains(title, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0)
            {
                return Envelope.Err(command, new AdapterError(ErrorCode.WindowNotFound, $"No window with title containing '{title}'")
                    .WithSuggestion("Run 'list-windows' to see available windows and their IDs.").Unsent());
            }
        }
        else if (app is not null)
        {
            candidates = windows;
            if (candidates.Count == 0)
            {
                return Envelope.Err(command, new AdapterError(ErrorCode.WindowNotFound, $"No windows found for app '{app}'")
                    .WithSuggestion("Run 'list-apps' to see available apps.").Unsent());
            }
        }
        else
        {
            return Envelope.Err(command, AdapterError.InvalidArgs("Provide --window-id, --app, or --title to identify the window"));
        }

        var window = WindowLookup.SelectWindow(
            candidates,
            new AdapterError(ErrorCode.WindowNotFound, "No matching window found"),
            "More than one window matches the focus target");

        using var lease = new InteractionLease(deadline, InteractionPolicy.Physical);
        _adapter.FocusWindow(window, lease);

        var writer = new EnvelopeWriter();
        writer.ObjectStart();
        writer.Key("focused");
        InventoryJson.WriteWindow(writer, window);
        writer.ObjectEnd();
        return Envelope.Ok(command, JsonNode.Parse(writer.Text()));
    }
}
