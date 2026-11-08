using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Cli;

/// <summary>Runs one parsed command: the policy preflight, then the command. A command the
/// adapter does not implement keeps the adapter's refusal, so a surface the platform does
/// not support fails closed.</summary>
public static class Dispatch
{
    /// <summary>Runs one command against the platform adapter and the state root's stores.</summary>
    public static EnvelopeResponse Run(
        ParsedCommand command,
        PlatformAdapter adapter,
        SnapshotStore snapshots,
        SessionStore sessions,
        string? environmentSession)
    {
        var refusal = CommandPolicy.Preflight(command);
        if (refusal is not null)
        {
            return Envelope.Err(command.Name, refusal);
        }

        var session = command.Session(environmentSession);

        try
        {
            return command.Name switch
            {
                "snapshot" => Snapshot(command, adapter, snapshots, sessions, session),
                "list-apps" => ListApps(adapter, command),
                "list-windows" => ListWindows(adapter, command),
                "version" => Version(),
                "click" => RefAction(command, adapter, snapshots, sessions, session),
                "double-click" => RefAction(command, adapter, snapshots, sessions, session),
                "triple-click" => RefAction(command, adapter, snapshots, sessions, session),
                "right-click" => RefAction(command, adapter, snapshots, sessions, session),
                "type" => RefAction(command, adapter, snapshots, sessions, session),
                "set-value" => RefAction(command, adapter, snapshots, sessions, session),
                "clear" => RefAction(command, adapter, snapshots, sessions, session),
                "focus" => RefAction(command, adapter, snapshots, sessions, session),
                "select" => RefAction(command, adapter, snapshots, sessions, session),
                "toggle" => RefAction(command, adapter, snapshots, sessions, session),
                "check" => RefAction(command, adapter, snapshots, sessions, session),
                "uncheck" => RefAction(command, adapter, snapshots, sessions, session),
                "expand" => RefAction(command, adapter, snapshots, sessions, session),
                "collapse" => RefAction(command, adapter, snapshots, sessions, session),
                "scroll" => RefAction(command, adapter, snapshots, sessions, session),
                "scroll-to" => RefAction(command, adapter, snapshots, sessions, session),
                _ => Envelope.Err(command.Name, AdapterError.NotSupported(command.Name)),
            };
        }
        catch (AdapterError error)
        {
            return Envelope.Err(command.Name, CommandPolicy.SettleUnknownDelivery(error, command.Name));
        }
    }

    private static EnvelopeResponse Snapshot(ParsedCommand command, PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions, string? session)
    {
        var scope = new WindowScope
        {
            WindowId = command.Value("window-id"),
            Title = command.Value("title"),
            Active = command.IsSet("active"),
            Frontmost = command.IsSet("frontmost"),
            NearX = command.Real("near-x"),
            NearY = command.Real("near-y"),
        };

        var options = TreeOptions.Default() with
        {
            MaxDepth = (byte)Math.Clamp(command.Integer("max-depth") ?? 10, 1, byte.MaxValue),
            IncludeBounds = command.IsSet("include-bounds"),
            InteractiveOnly = command.IsSet("interactive-only"),
            Compact = command.IsSet("compact"),
            Surface = SnapshotSurfaceNames.Parse(command.Value("surface") ?? "window") ?? SnapshotSurface.Window,
            Skeleton = command.IsSet("skeleton"),
        };

        return new SnapshotCommand(adapter, snapshots, sessions)
            .Run(command.Value("app")!, session, options, SnapshotCommand.MaxObservationNodes, scope);
    }

    private static EnvelopeResponse RefAction(ParsedCommand command, PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions, string? session)
    {
        var isScroll = command.Name == "scroll";
        var action = new Action
        {
            Kind = RefActionKinds.For(command.Name),
            Text = command.Positional.Count > 1 ? command.Positional[1] : null,
            Direction = DirectionFor(command.Value("direction") ?? (isScroll ? "down" : null)),
            Amount = (uint?)(command.Integer("amount") ?? (isScroll ? 3 : null)),
        };

        return new RefActionCommand(adapter, snapshots, sessions)
            .Run(command.Name, command.Ref!, action, session, command.IsSet("headed"));
    }

    private static Direction? DirectionFor(string? name) => name switch
    {
        "up" => Direction.Up,
        "down" => Direction.Down,
        "left" => Direction.Left,
        "right" => Direction.Right,
        _ => null,
    };

    private static EnvelopeResponse ListApps(PlatformAdapter adapter, ParsedCommand command)
    {
        var deadline = Deadline.Standard();
        var apps = adapter.ListApps(deadline);
        if (command.Value("app") is not null)
        {
            apps = apps.Where(app => app.MatchesIdentifier(command.Value("app")!)).ToList();
        }

        var writer = new EnvelopeWriter();
        InventoryJson.WriteApps(writer, apps);
        return Envelope.Ok("list-apps", JsonNode.Parse(writer.Text()));
    }

    private static EnvelopeResponse ListWindows(PlatformAdapter adapter, ParsedCommand command)
    {
        var windows = adapter.ListWindows(new WindowFilter { App = command.Value("app") }, Deadline.Standard());
        var writer = new EnvelopeWriter();
        InventoryJson.WriteWindows(writer, windows);
        return Envelope.Ok("list-windows", JsonNode.Parse(writer.Text()));
    }

    private static EnvelopeResponse Version()
    {
        var writer = new EnvelopeWriter();
        writer.ObjectStart();
        writer.Key("os");
        writer.String("windows");
        writer.Key("target");
        writer.String(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        writer.Key("version");
        writer.String(BuildInfo.Version);
        writer.ObjectEnd();
        return Envelope.Ok("version", JsonNode.Parse(writer.Text()));
    }
}

/// <summary>The action each ref command asks an element to perform.</summary>
public static class RefActionKinds
{
    public static ActionKind For(string name) => name switch
    {
        "click" => ActionKind.Click,
        "double-click" => ActionKind.DoubleClick,
        "triple-click" => ActionKind.TripleClick,
        "right-click" => ActionKind.RightClick,
        "type" => ActionKind.TypeText,
        "set-value" => ActionKind.SetValue,
        "clear" => ActionKind.Clear,
        "focus" => ActionKind.SetFocus,
        "select" => ActionKind.Select,
        "toggle" => ActionKind.Toggle,
        "check" => ActionKind.Check,
        "uncheck" => ActionKind.Uncheck,
        "expand" => ActionKind.Expand,
        "collapse" => ActionKind.Collapse,
        "scroll" => ActionKind.Scroll,
        "scroll-to" => ActionKind.ScrollTo,
        _ => ActionKind.Click,
    };
}
