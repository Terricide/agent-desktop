/// <summary>
/// The clock a frame loop runs on, and when it is allowed to stop early.
/// Both the travel and the click flourish sample a clock rather than counting
/// frames, so a dropped frame changes smoothness and never the instant the
/// cursor lands. Holding the interval and the start instant together keeps
/// that one decision in one place rather than once per loop.
/// Stopping early is offered only to a loop that runs after its control has
/// been answered. A travel is what an action waits on before it dispatches,
/// so it plays to its end however much is queued behind it.
/// </summary>
namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>Whether a frame loop should play on, or give way to something newer.</summary>
internal enum Continuation
{
    Play,
    Stop,
}

internal sealed class Frames
{
    private readonly global::System.TimeSpan _interval;
    private readonly global::System.Diagnostics.Stopwatch _started;

    private Frames(global::System.TimeSpan interval)
    {
        _interval = interval;
        _started = global::System.Diagnostics.Stopwatch.StartNew();
    }

    internal static Frames At(uint refreshHz)
    {
        return new Frames(Schedule.FrameInterval(refreshHz));
    }

    internal ulong ElapsedMs()
    {
        return (ulong)_started.ElapsedMilliseconds;
    }

    internal void Wait()
    {
        global::System.Threading.Thread.Sleep(_interval);
    }

    /// <summary>
    /// Waits out one interval unless a newer control is already waiting to be
    /// served, in which case the caller stops instead of sleeping.
    /// The interrupt is asked before the sleep rather than after it, because the
    /// whole point is that a superseding control does not spend a frame waiting
    /// for a flourish nobody is looking at any more.
    /// </summary>
    internal Continuation WaitUnless(global::System.Func<bool> interrupt)
    {
        if (interrupt())
        {
            return Continuation.Stop;
        }

        Wait();
        return Continuation.Play;
    }
}
