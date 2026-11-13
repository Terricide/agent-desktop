namespace AgentDesktop.Windows;

/// <summary>Maps what a rung sent and read back onto a <see cref="DeliveryOutcome"/>.</summary>
internal static class DeliveryOutcomeRules
{
    /// <summary>A rung that sent a write and knows whether it read the effect back. A write that was
    /// not sent is not delivered, whatever a later read says.</summary>
    internal static DeliveryOutcome FromDelivery(bool delivered, bool verified) => (delivered, verified) switch
    {
        (false, _) => DeliveryOutcome.NotDelivered,
        (true, false) => DeliveryOutcome.DeliveredUnverified,
        (true, true) => DeliveryOutcome.DeliveredVerified,
    };

    /// <summary>A rung that judged the postcondition and may have read nothing at all. A read that
    /// could not answer leaves the outcome unobserved, which is not the same as a delivery that
    /// failed: the chain must not stop on it as if the effect were known.</summary>
    internal static DeliveryOutcome FromObservation(bool? verified) => verified switch
    {
        true => DeliveryOutcome.DeliveredVerified,
        false => DeliveryOutcome.DeliveredUnverified,
        null => DeliveryOutcome.DeliveredUnobserved,
    };
}
