namespace AgentDesktop.Core.Response;

/// <summary>
/// The delivery dispositions a command states when it has the evidence for them. Each factory names
/// one state: nothing reached the application, something reached it and was not read back, something
/// reached it and was read back. The retry half is derived from the delivery half, so a caller cannot
/// pair a delivered write with a safe retry.
/// </summary>
public static class Disposition
{
    /// <summary>The command stopped before anything reached the application. A retry is safe because
    /// the application's state is unchanged by the attempt.</summary>
    public static DeliverySemantics NotDelivered() => DeliverySemantics.NotDelivered();

    /// <summary>The command reached the application, and no postcondition read confirmed the effect.
    /// A retry is unsafe: the first attempt may have landed, so a second one can apply the effect
    /// twice.</summary>
    public static DeliverySemantics DeliveredUnverified() => DeliverySemantics.DeliveredUnverified();

    /// <summary>The command reached the application and a postcondition read confirmed the effect. A
    /// retry is unsafe for the same reason as an unverified delivery.</summary>
    public static DeliverySemantics DeliveredVerified() => DeliverySemantics.DeliveredVerified();

    /// <summary>The command cannot say whether anything reached the application, so the disposition
    /// stays unknown and a retry is not judged safe.</summary>
    public static DeliverySemantics Unknown() => DeliverySemantics.Unknown();

    /// <summary>The delivery half alone decides the pair, for a path that knows the delivery state
    /// and nothing else.</summary>
    public static DeliverySemantics From(Delivery delivery) => DeliverySemantics.From(delivery);
}
