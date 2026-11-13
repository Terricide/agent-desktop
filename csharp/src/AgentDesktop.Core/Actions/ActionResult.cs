using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Actions;

/// <summary>What an action delivery did: the action, the mechanisms tried, and the delivery evidence.</summary>
public sealed record ActionResult
{
    public string Action { get; init; } = "";
    public List<ActionStep> Steps { get; init; } = new();
    public DeliverySemantics Disposition { get; init; } = DeliverySemantics.Unknown();
    public JsonObject? Details { get; init; }

    public static ActionResult DeliveredUnverified(string action) =>
        new() { Action = action, Disposition = DeliverySemantics.DeliveredUnverified() };

    public static ActionResult DeliveredUnverified(string action, List<ActionStep> steps) =>
        new() { Action = action, Steps = steps, Disposition = DeliverySemantics.DeliveredUnverified() };

    public static ActionResult SatisfiedWithoutDelivery(string action) =>
        new() { Action = action, Disposition = DeliverySemantics.NotDelivered() };

    public static ActionResult SatisfiedWithoutDelivery(string action, List<ActionStep> steps) =>
        new() { Action = action, Steps = steps, Disposition = DeliverySemantics.NotDelivered() };

    public static ActionResult DeliveredVerified(string action, List<ActionStep> steps) =>
        new() { Action = action, Steps = steps, Disposition = DeliverySemantics.DeliveredVerified() };

    public ActionResult WithDetails(JsonObject details) => this with { Details = details };

    /// <summary>Builds the action result from the executed activation-chain steps, matching
    /// ActionResult::from_execution in crates/core/src/actions/action_result.rs.</summary>
    public static ActionResult FromExecution(Action action, List<ActionStep> steps)
    {
        var delivered = false;
        bool? verified = null;
        foreach (var step in steps)
        {
            if (step.Outcome != "succeeded")
            {
                continue;
            }

            delivered = true;
            if (step.Verified is { } flag)
            {
                verified = (verified ?? true) && flag;
            }
        }

        var name = action.Name();
        if (!delivered)
        {
            return SatisfiedWithoutDelivery(name, steps);
        }

        if (verified != true)
        {
            return DeliveredUnverified(name, steps);
        }

        var result = DeliveredVerified(name, steps);
        return action.WritesElementValue()
            ? result.WithDetails(new JsonObject
            {
                ["verification_scope"] = "element_value",
                ["application_commit"] = "not_verified",
            })
            : result;
    }
}
