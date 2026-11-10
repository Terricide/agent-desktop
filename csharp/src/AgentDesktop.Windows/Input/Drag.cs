using System.Text.Json.Nodes;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Input;

/// <summary>`SendInput` drag synthesis with a release guard. The bare-coordinate path has no
/// window-identity gate, because there is no target window to verify; the headed flag is the
/// guard, enforced upstream. The guard arms immediately before the mouse-down and disarms only
/// after the final release posts at the destination; on any abort in between, its disposal posts
/// corrective input at the origin. `SendInput`'s return value is never treated as delivery
/// evidence, so every post here is fire-and-forget.</summary>
internal static class Drag
{
    internal const long DefaultDurationMs = 300;

    internal const long PickupDelayMs = 200;

    /// <summary>The sequence settles for a pickup delay twice: once after moving to the origin
    /// so the target sees the cursor arrive before the button, and once after the button lands
    /// so it registers the press before travel begins. The preflight reserves both, because a
    /// gate that reserves one admits a drag it cannot finish and the shortfall is spent with the
    /// button down.</summary>
    internal const long PickupTotalMs = PickupDelayMs * 2;

    internal const long DefaultDropDelayMs = 500;

    internal const long DwellTickMs = 16;

    internal const long MaxSteps = 4_096;

    internal const long MaxDragMs = 60_000;

    internal const long MaxDropDelayMs = 30_000;

    internal static void SynthesizeDrag(DragParams parameters, Deadline deadline) =>
        SynthesizeDrag(parameters.From, parameters.To, DurationOf(parameters), DropDelayOf(parameters), deadline);

    internal static void SynthesizeDrag(Point from, Point to, long? durationMs, long? dropDelayMs, Deadline deadline)
    {
        ValidateDrag(from, to, durationMs, dropDelayMs);
        var duration = durationMs ?? DefaultDurationMs;
        var dropDelay = dropDelayMs ?? DefaultDropDelayMs;
        PreflightDrag(duration, dropDelay, deadline);
        DragSequence(from, to, duration, dropDelay, deadline);
    }

    private static void DragSequence(Point origin, Point destination, long durationMs, long dropDelayMs, Deadline deadline)
    {
        var steps = Math.Clamp((durationMs + DwellTickMs - 1) / DwellTickMs, 1, MaxSteps);
        var stepDelay = TimeSpan.FromSeconds(durationMs / (double)steps / 1_000.0);

        deadline.EnsureRemaining();
        DragPost.Post(new DragEvent(origin, ButtonDown: false, ButtonUp: false));
        deadline.EnsureRemaining();
        SleepBounded(deadline, TimeSpan.FromMilliseconds(PickupDelayMs));

        using var guard = new DragReleaseGuard(origin);
        try
        {
            guard.Arm();
            DragPost.Post(new DragEvent(null, ButtonDown: true, ButtonUp: false));
            guard.MarkDelivered();

            deadline.EnsureRemaining();
            SleepBounded(deadline, TimeSpan.FromMilliseconds(PickupDelayMs));

            for (long index = 1; index <= steps; index++)
            {
                var progress = index / (double)steps;
                deadline.EnsureRemaining();
                DragPost.Post(new DragEvent(Interpolate(origin, destination, progress), false, false));
                guard.MarkDelivered();
                SleepBounded(deadline, stepDelay);
            }

            DwellOverDestination(destination, dropDelayMs, deadline, guard);

            deadline.EnsureRemaining();
            DragPost.Post(new DragEvent(destination, ButtonDown: false, ButtonUp: false));
            guard.MarkDelivered();
            DragPost.Post(new DragEvent(null, ButtonDown: false, ButtonUp: true));
            guard.MarkDelivered();
            guard.Disarm();
            deadline.EnsureRemaining();
        }
        catch (AdapterError error)
        {
            throw guard.EnrichError(error);
        }
    }

    private static void DwellOverDestination(Point destination, long delayMs, Deadline deadline, DragReleaseGuard guard)
    {
        var remainingMs = delayMs;
        while (remainingMs > 0)
        {
            deadline.EnsureRemaining();
            DragPost.Post(new DragEvent(destination, ButtonDown: false, ButtonUp: false));
            guard.MarkDelivered();
            var tickMs = Math.Min(remainingMs, DwellTickMs);
            SleepBounded(deadline, TimeSpan.FromMilliseconds(tickMs));
            remainingMs -= tickMs;
        }
    }

    internal static Point Interpolate(Point from, Point to, double progress) =>
        new(from.X + (to.X - from.X) * progress, from.Y + (to.Y - from.Y) * progress);

    /// <summary>Sleeps for the requested span, capped at the remaining budget, then fails with
    /// the shared timeout once the budget is spent.</summary>
    internal static void SleepBounded(Deadline deadline, TimeSpan duration)
    {
        var remaining = TimeSpan.FromMilliseconds(deadline.RemainingMs());
        var capped = duration < remaining ? duration : remaining;
        if (capped > TimeSpan.Zero)
        {
            Thread.Sleep(capped);
        }

        deadline.EnsureRemaining();
    }

    private static void PreflightDrag(long durationMs, long dropDelayMs, Deadline deadline)
    {
        long requiredMs;
        try
        {
            requiredMs = checked(PickupTotalMs + durationMs + dropDelayMs);
        }
        catch (OverflowException)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "Drag timing is too large");
        }

        var remainingMs = deadline.RemainingMs();
        if (remainingMs < requiredMs)
        {
            throw AdapterError.Timeout("Drag cannot complete within the remaining deadline")
                .WithDetails(new JsonObject
                {
                    ["physical_delivery_started"] = false,
                    ["required_ms"] = requiredMs,
                    ["remaining_ms"] = remainingMs,
                })
                .WithDisposition(DeliverySemantics.NotDelivered());
        }
    }

    internal static void ValidateDrag(Point from, Point to, long? durationMs, long? dropDelayMs)
    {
        ValidatePoint(from);
        ValidatePoint(to);
        if (durationMs is { } duration && duration > MaxDragMs)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, $"Drag duration must not exceed {MaxDragMs}ms");
        }

        if (dropDelayMs is { } dropDelay && dropDelay > MaxDropDelayMs)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, $"Drag drop delay must not exceed {MaxDropDelayMs}ms");
        }
    }

    private static long? DurationOf(DragParams parameters) => throw AdapterError.NotSupported("drag");

    private static long? DropDelayOf(DragParams parameters) => throw AdapterError.NotSupported("drag");

    internal static void ValidatePoint(Point point)
    {
        const double maxCoordinate = 10_000_000.0;
        if (double.IsFinite(point.X) && double.IsFinite(point.Y)
            && Math.Abs(point.X) <= maxCoordinate && Math.Abs(point.Y) <= maxCoordinate)
        {
            return;
        }

        throw AdapterError.InvalidArgs("Point coordinates must be finite and within platform geometry bounds");
    }
}
