using System.Collections.Generic;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The frame times a renderer is due to paint, as a schedule it can rebuild from the motion it was
/// given.
/// </summary>
internal sealed class Schedule
{
    private const int MaxPendingFrames = 64;
    private const uint MinRefreshHz = 24;
    private const uint MaxRefreshHz = 240;
    private const uint FallbackRefreshHz = 60;
    private const double HighlightRise = 0.08;
    private const double HighlightFall = 0.35;

    private readonly Queue<ulong> _pending = new();

    private ulong _next;

    /// <summary>
    /// The interval a frame loop sleeps for, from the refresh rate the screen reported.
    /// </summary>
    internal static global::System.TimeSpan FrameInterval(uint reportedHz) =>
        global::System.TimeSpan.FromMilliseconds(1000.0 / ClampRefresh(reportedHz));

    /// <summary>
    /// The refresh rate a frame loop may be built on. <c>GetDeviceCaps</c> answers <c>VREFRESH</c> for the
    /// screen DC, but the value is a driver's report: a zero would produce an infinite frame rate and an absurd
    /// value a budget the paint cannot meet, so both ends are bounded here and no caller has to remember to.
    /// </summary>
    internal static uint ClampRefresh(uint reportedHz)
    {
        if (reportedHz < MinRefreshHz)
        {
            return FallbackRefreshHz;
        }

        return global::System.Math.Min(reportedHz, MaxRefreshHz);
    }

    /// <summary>
    /// Whether the travel is over, given how long it has been running and how long core said it would take. A
    /// motion with nowhere to go arrives at once, so a click on the cursor's current position does not wait.
    /// </summary>
    internal static bool HasArrived(ulong elapsedMs, ulong motionDurationMs) =>
        elapsedMs >= motionDurationMs;

    /// <summary>
    /// The highlight's opacity across its hold: it rises, holds, and falls. Returns zero before it starts and
    /// after it ends, so a caller can drive the whole life of the cue from one number.
    /// </summary>
    internal static double HighlightProgress(ulong elapsedMs, ulong holdMs)
    {
        if (holdMs == 0 || elapsedMs >= holdMs)
        {
            return 0.0;
        }

        var fraction = (double)elapsedMs / holdMs;
        if (fraction < HighlightRise)
        {
            return fraction / HighlightRise;
        }

        if (fraction > 1.0 - HighlightFall)
        {
            return (1.0 - fraction) / HighlightFall;
        }

        return 1.0;
    }

    /// <summary>
    /// The label card's opacity as it appears, on an ease-out curve. A card that blinked to full strength reads
    /// as a glitch rather than as something arriving; macOS ramps its opacity over the same span with Core
    /// Animation's ease-out, and <c>1 - (1 - t)^3</c> is that curve closely enough to be indistinguishable at
    /// this duration. The card reads as fully present once the reveal is over, so a caller can drive the whole
    /// life of the card from one number and stop asking after it settles.
    /// </summary>
    internal static double RevealProgress(ulong elapsedMs, ulong durationMs)
    {
        if (durationMs == 0 || elapsedMs >= durationMs)
        {
            return 1.0;
        }

        var remaining = 1.0 - (double)elapsedMs / durationMs;
        return 1.0 - remaining * remaining * remaining;
    }

    /// <summary>
    /// The opacity of the whole overlay at one step of the rest fade. Linear, matching the reference, which
    /// steps its window alpha down in even hops rather than easing. Easing the disappearance would draw
    /// attention to it, which is the opposite of what resting is for.
    /// </summary>
    internal static double RestFade(uint step, uint steps)
    {
        if (steps == 0)
        {
            return 0.0;
        }

        return (double)(steps >= step ? steps - step : 0u) / steps;
    }

    /// <summary>Frames still due, oldest first.</summary>
    internal int PendingCount => _pending.Count;

    /// <summary>Clears the schedule and starts the next frame at <paramref name="now"/>.</summary>
    internal void Reset(ulong now)
    {
        _pending.Clear();
        _next = now;
    }

    /// <summary>Whether a frame is due at <paramref name="now"/>.</summary>
    internal bool HasDue(ulong now) => _pending.Count > 0 && _pending.Peek() <= now;

    /// <summary>
    /// Takes the due frame, dropping any earlier one the renderer missed.
    /// </summary>
    /// <remarks>
    /// A renderer that misses a frame paints the next one at the position the motion reports for the
    /// present moment, so a slow host stays smooth and the motion still lands on time.
    /// </remarks>
    internal ulong PopDue(ulong now)
    {
        ulong frame = _pending.Dequeue();
        while (_pending.Count > 0 && _pending.Peek() <= now)
        {
            _pending.Dequeue();
        }

        _next = frame;
        return frame;
    }

    /// <summary>
    /// Builds the schedule for one motion: the travel frames, the dwell, and the ripple frames.
    /// </summary>
    /// <remarks>
    /// The schedule is capped at <c>MaxPendingFrames</c> so a renderer that cannot keep up drops frames
    /// rather than accumulating a backlog it will never drain.
    /// </remarks>
    internal void Build(ulong start, ulong duration, ulong dwell, ulong total, ulong interval)
    {
        _pending.Clear();
        _next = start;
        if (interval == 0)
        {
            _pending.Enqueue(start);
            return;
        }

        ulong frame = start;
        while (frame <= start + total && _pending.Count < MaxPendingFrames)
        {
            _pending.Enqueue(frame);
            frame += interval;
        }

        if (dwell > 0)
        {
            _pending.Enqueue(start + duration + dwell);
        }

        if (total > duration + dwell)
        {
            ulong ripple = start + duration + dwell;
            while (ripple < start + total && _pending.Count < MaxPendingFrames)
            {
                ripple += interval;
                _pending.Enqueue(ripple);
            }
        }

        _next = start;
    }
}
