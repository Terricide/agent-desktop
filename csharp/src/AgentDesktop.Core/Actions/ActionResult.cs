using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Actions;

/// <summary>What an action delivery did: the action, the mechanisms tried, and the delivery evidence.</summary>
public sealed record ActionResult
{
    public string Action { get; init; } = "";
    public List<ActionStep> Steps { get; init; } = new();
    public DeliverySemantics Disposition { get; init; } = DeliverySemantics.Unknown();

    public static ActionResult DeliveredUnverified(string action) =>
        new() { Action = action, Disposition = DeliverySemantics.DeliveredUnverified() };

    public static ActionResult SatisfiedWithoutDelivery(string action) =>
        new() { Action = action, Disposition = DeliverySemantics.NotDelivered() };

    public static ActionResult DeliveredVerified(string action, List<ActionStep> steps) =>
        new() { Action = action, Steps = steps, Disposition = DeliverySemantics.DeliveredVerified() };
}
