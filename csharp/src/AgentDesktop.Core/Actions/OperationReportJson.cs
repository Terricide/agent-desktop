using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Actions;

/// <summary>The operation report as the command's data: the action, its delivery disposition, the steps
/// the adapter ran, any details it attached, and the target the adapter validated. Port of the serde
/// shape of <c>OperationReport</c> in <c>crates/core/src/system/operation_report.rs</c>, where the action
/// result is flattened and the target is tagged by kind.</summary>
public static class OperationReportJson
{
    public static JsonObject Data(OperationReport report)
    {
        var data = new JsonObject
        {
            ["action"] = report.Result.Action,
            ["disposition"] = new JsonObject
            {
                ["delivery"] = report.Result.Disposition.Delivery.AsString(),
                ["retry"] = report.Result.Disposition.Retry.AsString(),
            },
            ["target"] = Target(report.Target),
        };

        if (report.Result.Steps.Count > 0)
        {
            var steps = new JsonArray();
            foreach (var step in report.Result.Steps)
            {
                var member = new JsonObject
                {
                    ["label"] = step.Label,
                    ["outcome"] = step.Outcome,
                    ["mechanism"] = step.Mechanism,
                };
                if (step.Verified is { } verified)
                {
                    member["verified"] = verified;
                }

                steps.Add((JsonNode)member);
            }

            data["steps"] = steps;
        }

        if (report.Result.Details is not null)
        {
            data["details"] = report.Result.Details.DeepClone();
        }

        return data;
    }

    private static JsonObject Target(OperationTarget target) => target.Kind switch
    {
        "window" => Window(target),
        "app" => App(target),
        "pointer" => new JsonObject
        {
            ["kind"] = "pointer",
            ["point"] = Point(target.Pointer),
        },
        "drag" => new JsonObject
        {
            ["kind"] = "drag",
            ["from"] = Point(target.From),
            ["to"] = Point(target.To),
        },
        "clipboard" => new JsonObject { ["kind"] = "clipboard" },
        _ => new JsonObject { ["kind"] = target.Kind },
    };

    private static JsonObject Window(OperationTarget target) => new()
    {
        ["kind"] = "window",
        ["id"] = target.Window!.Id,
        ["title"] = target.Window.Title,
        ["process"] = Process(target.Window.Process),
    };

    private static JsonObject App(OperationTarget target) => new()
    {
        ["kind"] = "app",
        ["name"] = target.App!.Name,
        ["process"] = Process(target.App.Process),
    };

    private static JsonObject Process(ProcessIdentity process) => new()
    {
        ["pid"] = process.Pid.Value,
        ["instance"] = process.Instance,
    };

    private static JsonObject Point(Point? point) => new()
    {
        ["x"] = point!.X,
        ["y"] = point.Y,
    };
}
