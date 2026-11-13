using System.Text.Json.Nodes;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Actions;

/// <summary>Builds the pointer and drag operation reports. A pointer event is verified only when the
/// adapter read the cursor position back; a drag never claims verification, because nothing reads the
/// receiving application's state after the release.</summary>
public static class OperationReportBuild
{
    private const string PhysicalSynthetic = "physical_synthetic";

    private const string Succeeded = "succeeded";

    public static OperationReport PointerDispatched(MouseEvent mouseEvent, bool positionVerified)
    {
        var (action, label) = mouseEvent.Kind switch
        {
            "move" => ("mouse-move", "pointer_move"),
            "down" => ("mouse-down", "pointer_down"),
            "up" => ("mouse-up", "pointer_up"),
            "click" => ("mouse-click", "pointer_click"),
            "wheel" => ("mouse-wheel", "pointer_wheel"),
            _ => throw AdapterError.InvalidArgs($"Unknown mouse event kind '{mouseEvent.Kind}'"),
        };

        var target = new OperationTarget { Kind = "pointer", Pointer = mouseEvent.Point };
        var step = PhysicalStep(label);
        if (!positionVerified)
        {
            return new OperationReport
            {
                Result = ActionResult.DeliveredUnverified(action, [step]),
                Target = target,
            };
        }

        var verified = ActionResult.DeliveredUnverified(action, [step with { Verified = true }])
            .WithDetails(new JsonObject { ["verification_scope"] = "pointer_position" });
        return new OperationReport
        {
            Result = WithVerifiedDelivery(verified),
            Target = target,
        };
    }

    public static OperationReport DragDispatched(DragParams parameters) => new()
    {
        Result = ActionResult.DeliveredUnverified("drag", [PhysicalStep("pointer_drag")]),
        Target = new OperationTarget { Kind = "drag", From = parameters.From, To = parameters.To },
    };

    /// <summary>Builds the report for one window write. The step is the platform call, so its mechanism
    /// is the semantic API, not synthetic input. The read-back is the only proof the write landed: a
    /// write with no read-back is reported as delivered-unverified, and a write whose read-back agrees
    /// is reported as delivered-verified with the scope and tolerance it was measured against.</summary>
    public static OperationReport WindowOperation(OperationTarget target, WindowOp op, WindowReadback? readback)
    {
        var step = new ActionStep(op.StepLabel(), Succeeded, SemanticApi);
        if (readback is null)
        {
            return new OperationReport
            {
                Result = ActionResult.DeliveredUnverified(op.CommandName(), [step]),
                Target = target,
            };
        }

        var verified = ActionResult.DeliveredUnverified(op.CommandName(), [step with { Verified = true }])
            .WithDetails(readback.Details());
        return new OperationReport
        {
            Result = WithVerifiedDelivery(verified),
            Target = target,
        };
    }

    private static ActionResult WithVerifiedDelivery(ActionResult result) =>
        result.Disposition == DeliverySemantics.DeliveredUnverified()
            ? result with { Disposition = DeliverySemantics.DeliveredVerified() }
            : result;

    private static ActionStep PhysicalStep(string label) => new(label, Succeeded, PhysicalSynthetic);

    private const string SemanticApi = "semantic_api";
}
