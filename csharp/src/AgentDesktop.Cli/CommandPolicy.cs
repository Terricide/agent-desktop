using AgentDesktop.Core.Response;

namespace AgentDesktop.Cli;

/// <summary>The preflight a command passes before it reaches the application: its arguments
/// are checked, and the permissions it needs are granted. UI Automation needs no
/// user-granted permission, so on Windows the argument check is the gate and every
/// permission check passes.</summary>
public static class CommandPolicy
{
    /// <summary>The refusal a command earns before dispatch, or null when it may run.</summary>
    public static AdapterError? Preflight(ParsedCommand command)
    {
        if (RefCommands.Contains(command.Name) && command.Ref is null)
        {
            return AdapterError.InvalidArgs(
                $"'{command.Name}' needs a snapshot-qualified ref, for example @s1a2b:e1");
        }

        if (command.Name is "set-value" or "select" or "type" && command.Positional.Count < 2)
        {
            return AdapterError.InvalidArgs($"'{command.Name}' needs a value after the ref");
        }

        if (command.Name == "press" && command.Positional.Count < 1)
        {
            return AdapterError.InvalidArgs("'press' needs a key combo, for example ctrl+c")
                .WithSuggestion("Run 'press <combo>' with a +-separated combo, or press <combo> <ref> to target an element");
        }

        if (command.Name == "scroll"
            && command.Value("direction") is { } direction
            && direction is not ("up" or "down" or "left" or "right"))
        {
            return AdapterError.InvalidArgs($"Invalid scroll direction '{direction}'. Use up, down, left, or right");
        }

        if (command.Name == "find"
            && (command.Value("root") is not null || command.Value("surface") is not null))
        {
            return AdapterError.NotSupported("find --root and find --surface");
        }

        return null;
    }

    /// <summary>Whether the command only reads state and sends nothing to the application. A
    /// command that sends input is a write even when it takes no ref, so `press` is a write: its
    /// key events reach the frontmost window with no element to gate on.</summary>
    public static bool IsReadOnly(string name) => !WriteCommands.Contains(name);

    /// <summary>Settles the delivery of an error that carries none: a read-only command or
    /// invalid-args refusal never sent an action to the application.</summary>
    public static AdapterError SettleUnknownDelivery(AdapterError error, string commandName) =>
        IsReadOnly(commandName) || error.Code == ErrorCode.InvalidArgs ? error.Unsent() : error;

    /// <summary>The commands whose first positional value is a ref. A ref already carries
    /// its source window, so these take no window scope.</summary>
    private static readonly HashSet<string> RefCommands = new()
    {
        "click", "double-click", "triple-click", "right-click", "type", "set-value",
        "clear", "focus", "select", "toggle", "check", "uncheck", "expand", "collapse",
        "scroll", "scroll-to",
    };

    /// <summary>The commands that send something to an application.</summary>
    private static readonly HashSet<string> WriteCommands = new(RefCommands)
    {
        "press", "key-down", "key-up", "hover", "drag", "mouse-move", "mouse-click",
        "mouse-down", "mouse-up", "mouse-wheel", "clipboard-set", "clipboard-clear",
    };
}
