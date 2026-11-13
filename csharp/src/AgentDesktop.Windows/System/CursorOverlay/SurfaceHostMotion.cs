using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>The motion half of the renderer: where a control sends the cursor, how the card appears, and how the click flourish plays.</summary>
internal sealed partial class SurfaceHost
{
    private void ApplyPresent(CursorOverlayInstruction? instruction)
    {
        if (instruction is null)
        {
            return;
        }

        switch (instruction.Phase)
        {
            case CursorPhase.Travel:
                _presentation.Depart(instruction.IsPointer);
                Travel(instruction.Destination, instruction.Target);
                break;
            case CursorPhase.Drag:
                TakeDragOrigin(instruction.DragFrom ?? instruction.Destination, instruction.Target);
                break;
            case CursorPhase.Caret:
            case CursorPhase.CaretFollow:
                _caret.Place(
                    instruction.Target,
                    _presentation.Style.AccentRgb(),
                    instruction.Phase == CursorPhase.CaretFollow);
                break;
            case CursorPhase.Effect:
                break;
        }
    }

    private void SettlePresent(CursorOverlayInstruction? instruction, global::System.Func<bool> interrupt)
    {
        if (instruction is null)
        {
            return;
        }

        switch (instruction.Phase)
        {
            case CursorPhase.Travel:
            case CursorPhase.Drag:
                PlayReveal(instruction.Target, interrupt);
                break;
            case CursorPhase.Effect:
                Effect(instruction.Destination, instruction.Target, instruction.IsClick, interrupt);
                break;
            case CursorPhase.Caret:
            case CursorPhase.CaretFollow:
                break;
        }
    }

    /// <summary>
    /// Travels to where a drag begins and stays there.
    /// </summary>
    /// <remarks>
    /// The reference arms pointer tracking here, so its overlay follows the real pointer for the length of the
    /// drag and draws a trail behind it. This does not, and the reason is the platform's own rule rather than an
    /// omission: a drag is physical input, headed actions hand the pointer to the operator, and this overlay is
    /// suppressed for the rest of a session that takes one. Standing at the origin is the whole of what can be
    /// shown truthfully, and it is shown before the acknowledgement because a drag is a pre-dispatch control
    /// that its caller blocks on.
    /// </remarks>
    private void TakeDragOrigin(Point origin, Rect? target)
    {
        _presentation.Depart(false);
        Travel(origin, target);
    }

    /// <summary>
    /// Plays the motion core computed, then leaves the cursor at its destination.
    /// </summary>
    /// <remarks>
    /// Deliberately unstoppable: this is the one loop an action is blocked on, so giving it up for a newer
    /// control would dispatch a click at wherever the cursor had got to.
    /// </remarks>
    private void Travel(Point destination, Rect? target)
    {
        _caret.HideWhenLeaving(_presentation.Pose, destination);
        var motion = CursorMotion.New(_presentation.Pose, destination).WithImpact(false).WithRipple(false);
        var frames = Frames.At(_topology.RefreshHz);
        while (true)
        {
            var elapsed = frames.ElapsedMs();
            _presentation.Pose = motion.Pose(elapsed).Point;
            Draw(0.0, target, 0.0);
            if (Schedule.HasArrived(elapsed, motion.Duration()))
            {
                break;
            }

            frames.Wait();
        }

        _presentation.Pose = destination;
        _presentation.Arrive();
        Draw(0.0, target, 0.0);
        BeginReveal();
    }

    /// <summary>
    /// The click flourish and the outline, after dispatch has already confirmed. Fire-and-forget by contract, so
    /// nothing waits on it and anything newer takes it over.
    /// </summary>
    private void Effect(Point destination, Rect? target, bool click, global::System.Func<bool> interrupt)
    {
        _presentation.Pose = destination;
        BeginReveal();
        var motion = CursorMotion.New(destination, destination)
            .WithImpact(click)
            .WithRipple(_presentation.Style.Ripple());
        var frames = Frames.At(_topology.RefreshHz);
        while (true)
        {
            var elapsed = frames.ElapsedMs();
            var pose = motion.Pose(elapsed);
            var opacity = Schedule.HighlightProgress(elapsed, CursorOverlayTiming.HighlightHoldMs);
            Draw(pose.Ripple, target, opacity);
            if (elapsed >= global::System.Math.Max(motion.Total(), CursorOverlayTiming.HighlightHoldMs))
            {
                Draw(0.0, null, 0.0);
                return;
            }

            if (frames.WaitUnless(interrupt) == Continuation.Stop)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Draws frames until the card has finished appearing. Nothing waits on this — the cursor has already
    /// arrived and the action has already been acknowledged — so a newer control takes it over.
    /// </summary>
    private void PlayReveal(Rect? target, global::System.Func<bool> interrupt)
    {
        var frames = Frames.At(_topology.RefreshHz);
        while (_presentation.Reveal.IsPlaying())
        {
            Draw(0.0, target, 0.0);
            if (frames.WaitUnless(interrupt) == Continuation.Stop)
            {
                return;
            }
        }

        Draw(0.0, target, 0.0);
    }

}
