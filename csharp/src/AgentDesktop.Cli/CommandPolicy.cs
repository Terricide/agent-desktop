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

        if (command.Name == "snapshot" && command.Value("app") is null)
        {
            return AdapterError.InvalidArgs("snapshot needs --app <name>")
                .WithSuggestion("Run 'list-apps' and retry with --app <name>");
        }

        return null;
    }

    /// <summary>Whether the command only reads state and sends nothing to the application.</summary>
    public static bool IsReadOnly(string name) => !RefCommands.Contains(name);

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
}