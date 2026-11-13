using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Actions;

/// <summary>The adapter's own report for a pointer, drag, clipboard, app-close, or window operation. The command returns it as data.</summary>
public sealed record OperationReport
{
    public ActionResult Result { get; init; } = new();
    public OperationTarget Target { get; init; } = new();

    public DeliverySemantics Disposition() => Result.Disposition;

    public static OperationReport DeliveredUnverified(string action, OperationTarget target) =>
        new() { Result = ActionResult.DeliveredUnverified(action), Target = target };

    public static OperationReport SatisfiedWithoutDelivery(string action, OperationTarget target) =>
        new() { Result = ActionResult.SatisfiedWithoutDelivery(action), Target = target };
}
