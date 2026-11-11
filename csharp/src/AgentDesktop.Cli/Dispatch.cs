using System.Text.Json;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Locator;
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
                "find" => Find(command, adapter, snapshots, sessions, session),
                "list-apps" => ListApps(adapter, command),
                "list-displays" => ListDisplays(adapter),
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
                "press" => KeyCommand(command, ActionKind.PressKey, adapter, snapshots, sessions),
                "key-down" => KeyCommand(command, ActionKind.KeyDown, adapter, snapshots, sessions),
                "key-up" => KeyCommand(command, ActionKind.KeyUp, adapter, snapshots, sessions),
                "mouse-move" => PointerCommand(command, "move", adapter),
                "mouse-down" => PointerCommand(command, "down", adapter),
                "mouse-up" => PointerCommand(command, "up", adapter),
                "mouse-click" => PointerCommand(command, "click", adapter),
                "mouse-wheel" => PointerCommand(command, "wheel", adapter),
                "drag" => DragCommand(command, adapter),
                "permissions" => PermissionsCommand(command, adapter),
                "clipboard-get" => ClipboardGet(command, adapter),
                "clipboard-set" => ClipboardSet(command, adapter),
                "clipboard-clear" => ClipboardClear(command, adapter),
                _ => Envelope.Err(command.Name, AdapterError.NotSupported(command.Name)),
            };
        }
        catch (AdapterError error)
        {
            return Envelope.Err(command.Name, CommandPolicy.SettleUnknownDelivery(error, command.Name));
        }
    }

    private static EnvelopeResponse Find(ParsedCommand command, PlatformAdapter adapter, SnapshotStore snapshots, SessionStore sessions, string? session) =>
        new FindCommand(adapter, snapshots, sessions).Run(
            new FindArgs
            {
                Scope = new FindScopeArgs
                {
                    App = command.Value("app"),
                    WindowId = command.Value("window-id"),
                    Root = command.Value("root"),
                },
                Filter = new FindFilterArgs
                {
                    Role = command.Value("role"),
                    Name = command.Value("name"),
                    Description = command.Value("description"),
                    NativeId = command.Value("native-id"),
                    Value = command.Value("value"),
                    Text = command.Value("text"),
                    Exact = command.IsSet("exact"),
                },
                StateFlags = command.Value("state")?.Split(',').ToList() ?? new List<string>(),
                Selection = new FindSelectionArgs
                {
                    Count = command.IsSet("count"),
                    First = command.IsSet("first"),
                    Last = command.IsSet("last"),
                    Nth = command.Integer("nth"),
                    Limit = command.Integer("limit"),
                },
                TimeoutMs = command.Integer("timeout-ms"),
            },
            session);

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

    /// <summary>`press` with no ref targets the frontmost window, so the combo is the first
    /// positional and there is no ref to resolve.</summary>
    /// <summary>Runs a key command that names no element: `press`, `key-down` or `key-up`.
    /// All three take one `+`-separated combo and reach the adapter with a null handle, the
    /// global key target. The adapter decides which of the three it will honour.</summary>
    private static EnvelopeResponse KeyCommand(
        ParsedCommand command,
        ActionKind kind,
        PlatformAdapter adapter,
        SnapshotStore snapshots,
        SessionStore sessions)
    {
        var combo = Combo.Parse(command.Positional[0]);
        var refusal = KeyComboGate.Refusal(combo, command.Positional[0], command.IsSet("force"), adapter);
        if (refusal is not null)
        {
            return Envelope.Err(command.Name, refusal);
        }

        return new RefActionCommand(adapter, snapshots, sessions)
            .RunGlobal(command.Name, new Action { Kind = kind, Key = combo }, command.IsSet("headed"));
    }

    /// <summary>A standalone pointer event names absolute coordinates as `x,y` and a button, and reaches
    /// the adapter as a `MouseEvent`. The wheel carries its notch count as the vertical delta, so the
    /// `--amount` the caller states is the delta, not a scroll unit count.</summary>
    private static EnvelopeResponse PointerCommand(ParsedCommand command, string kind, PlatformAdapter adapter)
    {
        Point? point = ParsePoint(command.Value("xy"));
        if (point is null)
        {
            return Envelope.Err(command.Name, AdapterError.InvalidArgs("--xy x,y is required for " + command.Name));
        }

        var mouseEvent = new MouseEvent
        {
            Kind = kind,
            Point = point,
            Button = command.Value("button") ?? "left",
            ClickCount = command.Integer("clicks") ?? 1,
            DeltaY = kind == "wheel" ? command.Real("amount") ?? 0 : 0,
            DeltaX = kind == "wheel" ? command.Real("dx") ?? 0 : 0,
        };

        using var lease = new InteractionLease(Deadline.Standard(), InteractionPolicy.Physical);
        return Envelope.Ok(command.Name, ReportData(adapter.MouseEvent(mouseEvent, lease)));
    }

    /// <summary>`drag` names two absolute coordinates as `x,y` and `x,y`.</summary>
    private static EnvelopeResponse DragCommand(ParsedCommand command, PlatformAdapter adapter)
    {
        Point? from = ParsePoint(command.Value("from"));
        Point? to = ParsePoint(command.Value("to"));
        if (from is null || to is null)
        {
            return Envelope.Err(command.Name, AdapterError.InvalidArgs("--from x,y and --to x,y are required for drag"));
        }

        var parameters = new DragParams
        {
            From = from,
            To = to,
            DropDelayMs = command.Integer("drop-delay") ?? 0,
            Type = command.Value("type") ?? "",
        };

        using var lease = new InteractionLease(Deadline.Standard(), InteractionPolicy.Physical);
        return Envelope.Ok(command.Name, ReportData(adapter.Drag(parameters, lease)));
    }

    /// <summary>The adapter's report as data: the action, its delivery disposition, and the chain steps it
    /// ran. The same shape the ref action command prints, so a pointer command and a ref action look alike.</summary>
    private static JsonObject ReportData(OperationReport report)
    {
        var data = new JsonObject
        {
            ["action"] = report.Result.Action,
            ["disposition"] = new JsonObject
            {
                ["delivery"] = report.Result.Disposition.Delivery.AsString(),
                ["retry"] = report.Result.Disposition.Retry.AsString(),
            },
        };
        var steps = new JsonArray();
        foreach (var step in report.Result.Steps)
        {
            steps.Add(JsonNode.Parse("{\"label\":" + Escaped(step.Label)
                + ",\"outcome\":" + Escaped(step.Outcome)
                + ",\"mechanism\":" + Escaped(step.Mechanism) + "}"));
        }
        data["steps"] = steps;
        return data;
    }

    /// <summary>A step label is short ASCII, so it goes through the JSON string API and comes back quoted.</summary>
    private static string Escaped(string value) => JsonValue.Create(value).ToJsonString();

    /// <summary>Parses `x,y` into a point. A single number is rejected: a drag and a pointer event both name
    /// a position, and half a position is not a position.</summary>
    private static Point? ParsePoint(string? value)
    {
        if (value is null) return null;
        string[] parts = value.Split(',');
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out double x)) return null;
        if (!double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out double y)) return null;
        return new Point(x, y);
    }

    /// <summary>`clipboard-get` reads the pasteboard. A pasteboard with no text is a normal empty result,
    /// not a refusal, so the data says the text is absent.</summary>
    /// <summary>`permissions` reads the permission state the adapter can observe. `--request`
    /// asks the operating system for it, which is the path that can report a denial; the plain
    /// read never sends a request, so a state it reports as unknown stays unknown.</summary>
    private static EnvelopeResponse PermissionsCommand(ParsedCommand command, PlatformAdapter adapter)
    {
        if (command.IsSet("guide"))
        {
            return Envelope.Err(command.Name, AdapterError.NotSupported("start_permission_guide"));
        }

        using var lease = new InteractionLease(Deadline.Standard(), InteractionPolicy.Physical);
        var report = command.IsSet("request")
            ? adapter.RequestPermissions(lease)
            : adapter.PermissionReport(lease.Deadline());
        var data = new JsonObject
        {
            ["accessibility"] = StateJson(report.Accessibility),
            ["screen_recording"] = StateJson(report.ScreenRecording),
            ["automation"] = StateJson(report.Automation),
        };
        return Envelope.Ok(command.Name, data);
    }

    /// <summary>A permission state is an object tagged with its state, the shape the envelope
    /// contract uses at every permission site.</summary>
    private static JsonNode StateJson(PermissionState state) => new JsonObject
    {
        ["state"] = state switch
        {
            PermissionState.Granted => "granted",
            PermissionState.Denied => "denied",
            PermissionState.NotRequired => "not_required",
            PermissionState.Unknown => "unknown",
            _ => "unknown",
        },
    };

    private static EnvelopeResponse ClipboardGet(ParsedCommand command, PlatformAdapter adapter)
    {
        var content = adapter.GetClipboardContent(ClipboardFormat.Text, Deadline.Standard());
        var data = new JsonObject
        {
            ["format"] = "text",
            ["text"] = content?.Text,
        };
        if (content?.Text is string text)
        {
            data["length"] = text.Length;
        }

        return Envelope.Ok(command.Name, data);
    }

    /// <summary>`clipboard-set` writes the text given as the first positional.</summary>
    private static EnvelopeResponse ClipboardSet(ParsedCommand command, PlatformAdapter adapter)
    {
        if (command.Positional.Count == 0)
        {
            return Envelope.Err(command.Name, AdapterError.InvalidArgs("clipboard-set needs the text to write"));
        }

        using var lease = new InteractionLease(Deadline.Standard(), InteractionPolicy.Physical);
        return Envelope.Ok(command.Name, ReportData(adapter.SetClipboardContent(
            new ClipboardContent { Format = ClipboardFormat.Text, Text = command.Positional[0] }, lease)));
    }

    /// <summary>`clipboard-clear` empties the pasteboard.</summary>
    private static EnvelopeResponse ClipboardClear(ParsedCommand command, PlatformAdapter adapter)
    {
        using var lease = new InteractionLease(Deadline.Standard(), InteractionPolicy.Physical);
        return Envelope.Ok(command.Name, ReportData(adapter.ClearClipboard(lease)));
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

    /// <summary>`list-displays` writes the active monitors. The array is the whole data, the shape
    /// the Rust command writes, so a caller reads the same keys on both builds.</summary>
    private static EnvelopeResponse ListDisplays(PlatformAdapter adapter)
    {
        var writer = new EnvelopeWriter();
        InventoryJson.WriteDisplays(writer, adapter.ListDisplays(Deadline.Standard()));
        return Envelope.Ok("list-displays", JsonNode.Parse(writer.Text()));
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
