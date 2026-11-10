namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// The shape parameters one travel is drawn with, read off a profile for a given move index.
/// </summary>
internal sealed record PathShape
{
    public double BowScale { get; init; }

    public double Overshoot { get; init; }

    public double TremorPixels { get; init; }

    public ulong? Seed { get; init; }

    public ulong MoveIndex { get; init; }

    public static PathShape FromProfile(CursorMotionProfile profile, ulong moveIndex) => new()
    {
        BowScale = profile.Bow,
        Overshoot = profile.Overshoot,
        TremorPixels = profile.Tremor,
        Seed = profile.Seed,
        MoveIndex = moveIndex,
    };
}
