namespace AgentDesktop.Windows;

/// <summary>
/// What one rung of an activation chain established about delivery. The five states are the ones the
/// shared contract names: nothing was sent, the target was already in the wanted state, something was
/// sent and not read back, something was sent and read back, and something was sent with no read
/// attempted. The chain stops on any state except <c>NotDelivered</c>, which is the only state that
/// means the next rung may still work.
/// </summary>
internal enum DeliveryOutcome
{
    NotDelivered,
    SatisfiedNoDelivery,
    DeliveredUnverified,
    DeliveredVerified,
    DeliveredUnobserved,
}
