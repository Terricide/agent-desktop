namespace AgentDesktop.Core.Response;

/// <summary>What the command established about whether the operation reached the application.</summary>
public enum Delivery
{
    Unknown,
    NotDelivered,
    DeliveryUncertain,
    DeliveredUnverified,
    DeliveredVerified,
}

/// <summary>Whether an automatic retry is safe, derived from delivery.</summary>
public enum Retry
{
    Unknown,
    Safe,
    Unsafe,
}

/// <summary>The pair the envelope serializes as disposition.</summary>
public sealed record DeliverySemantics(Delivery Delivery, Retry Retry)
{
    public static DeliverySemantics Unknown() => new(Delivery.Unknown, Retry.Unknown);

    public static DeliverySemantics NotDelivered() => new(Delivery.NotDelivered, Retry.Safe);

    public static DeliverySemantics DeliveryUncertain() => new(Delivery.DeliveryUncertain, Retry.Unsafe);

    public static DeliverySemantics DeliveredUnverified() => new(Delivery.DeliveredUnverified, Retry.Unsafe);

    public static DeliverySemantics DeliveredVerified() => new(Delivery.DeliveredVerified, Retry.Unsafe);

    /// <summary>Retry follows from delivery: not_delivered is safe, every other known value is unsafe.</summary>
    public static Retry RetryFor(Delivery delivery) => delivery switch
    {
        Delivery.NotDelivered => Retry.Safe,
        Delivery.Unknown => Retry.Unknown,
        _ => Retry.Unsafe,
    };

    public static DeliverySemantics From(Delivery delivery) => new(delivery, RetryFor(delivery));
}
