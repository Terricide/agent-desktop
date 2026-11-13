using System;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// The trajectory a renderer samples: a function of elapsed milliseconds, so a dropped frame
/// changes smoothness and never the instant the cursor lands, which is the instant the action is
/// waiting on.
/// </summary>
public sealed class CursorMotion
{
    private const ulong RippleMs = 300;

    private HandPath Path { get; init; } = new HandPath();

    private ulong DurationMsValue { get; init; }

    private ulong DwellMsValue { get; init; }

    private bool Click { get; set; }

    private bool Ripple { get; set; } = true;

    public static CursorMotion New(Point start, Point destination) =>
        Shaped(start, destination, new CursorMotionProfile(), 0);

    /// <summary>
    /// Overlay motion tuned by <paramref name="profile"/>; <paramref name="moveIndex"/> varies seeded
    /// paths from one move to the next. A profile that fails validation falls back to the default
    /// motion.
    /// </summary>
    public static CursorMotion Shaped(Point start, Point destination, CursorMotionProfile profile, ulong moveIndex)
    {
        CursorMotionProfile effective = SafeProfile(profile);
        HandPath path = HandPath.Shaped(start, destination, PathShape.FromProfile(effective, moveIndex));
        return new CursorMotion
        {
            Path = path,
            DurationMsValue = path.DurationMs(effective.TravelMs()),
            DwellMsValue = effective.DwellMs,
            Click = false,
            Ripple = true,
        };
    }

    public CursorMotion WithImpact(bool click)
    {
        Click = click;
        return this;
    }

    public CursorMotion WithRipple(bool ripple)
    {
        Ripple = ripple;
        return this;
    }

    public ulong Duration() => DurationMsValue;

    public ulong Dwell() => DwellMsValue;

    public ulong Total() => Settled() + (PlaysRipple() ? RippleMs : 0);

    public Point Sample(ulong elapsedMs)
    {
        if (elapsedMs >= DurationMsValue)
        {
            return Path.DestinationPoint();
        }

        return Path.At((double)elapsedMs / DurationMsValue);
    }

    public CursorPose Pose(ulong elapsedMs)
    {
        Point point = Sample(Math.Min(elapsedMs, DurationMsValue));
        ulong settled = Settled();
        if (!PlaysRipple() || elapsedMs <= settled)
        {
            return CursorPose.Still(point);
        }

        return new CursorPose
        {
            Point = point,
            Ripple = Math.Clamp((double)(elapsedMs - settled) / RippleMs, 0.0, 1.0),
        };
    }

    private static CursorMotionProfile SafeProfile(CursorMotionProfile profile)
    {
        try
        {
            profile.Validate();
            return profile;
        }
        catch (Exception)
        {
            return new CursorMotionProfile();
        }
    }

    private ulong Settled() => DurationMsValue + DwellMsValue;

    private bool PlaysRipple() => Click && Ripple;
}
