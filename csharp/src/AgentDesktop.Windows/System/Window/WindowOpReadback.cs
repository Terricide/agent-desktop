namespace AgentDesktop.Windows.System;

using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;

/// <summary>
/// Picks the read-back that proves a window operation landed: the geometry a resize or move must settle within, or the
/// placement state a minimize, restore, or maximize must reach.
/// </summary>
/// <remarks>
/// Each operation maps to exactly one scope, and the scope names only what the adapter reads back from the window
/// placement. A maximize therefore claims the maximized state and says nothing about geometry, and a resize claims the
/// size it reads back and says nothing about position. Geometry scopes carry the placement tolerance in pixels,
/// because a window can settle a few pixels away from the rectangle requested. State scopes carry no tolerance.
/// <para>
/// <c>WindowOp</c>, <c>WindowReadback</c> and <c>WindowOpPlacement.PlacementTolerancePx</c> live in
/// <c>AgentDesktop.Core.Systems</c>, mirroring <c>window_op.rs</c>: the operation cases, the two read-back factories,
/// and the tolerance the placement re-read is verified against.
/// </para>
/// </remarks>
internal static class WindowOpReadback
{
    /// <summary>
    /// Returns the read-back that verifies <paramref name="op"/>. Every window operation has one.
    /// </summary>
    internal static WindowReadback SelectReadback(WindowOp op)
    {
        double tolerance = WindowOpPlacement.PlacementTolerancePx;
        return op switch
        {
            WindowOp.Resize => WindowReadback.Geometry("window_size", tolerance, "px"),
            WindowOp.Move => WindowReadback.Geometry("window_position", tolerance, "px"),
            WindowOp.Minimize or WindowOp.Restore => WindowReadback.State("window_minimized_state"),
            WindowOp.Maximize => WindowReadback.State("window_maximized_state"),
            _ => throw AdapterError.NotSupported("window_readback"),
        };
    }
}
