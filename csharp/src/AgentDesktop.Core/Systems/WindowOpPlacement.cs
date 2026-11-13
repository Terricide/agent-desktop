namespace AgentDesktop.Core.Systems;

/// <summary>
/// The re-read tolerance a window geometry write is verified against. A window settles a few pixels
/// away from the rectangle it was asked for: the compositor animates the move, and the read-back lands
/// inside that headroom rather than on the requested edge. The tolerance is the floor measured for that
/// headroom, so a write that reached the requested rectangle within it is a delivered write.
/// </summary>
public static class WindowOpPlacement
{
    /// <summary>The placement re-read tolerance in pixels, pinned by the measured DWM animation headroom.</summary>
    public const int PlacementTolerancePx = 8;
}
