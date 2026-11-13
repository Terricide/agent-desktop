using System;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// The travel a drawn cursor follows: a two-part reach with a bow and a settle-weighted tremor
/// across the straight line, so a move reads as a hand rather than a lerp.
/// </summary>
internal sealed record HandPath
{
    private const double PrimarySpan = 0.74;
    private const double CorrectionStart = 0.62;
    private const double BowRatio = 0.055;
    private const double MaxBow = 38.0;
    private const double TremorCycles = 7.0;
    private const ulong SideSalt = 0x5EED_0001;
    private const ulong BowSalt = 0x5EED_0002;
    private const ulong PaceSalt = 0x5EED_0003;

    private Point Start { get; init; } = new Point(0.0, 0.0);

    private Point Destination { get; init; } = new Point(0.0, 0.0);

    private double Bow { get; init; }

    private ulong Seed { get; init; }

    private double Reach { get; init; }

    private double TremorPixels { get; init; }

    private double Pace { get; init; }

    public static HandPath Shaped(Point start, Point destination, PathShape shape)
    {
        double distance = DistanceBetween(start, destination);
        ulong coordinateSeed = RotateLeft(Bit(start.X) ^ Bit(destination.Y), 17) ^ Bit(destination.X);
        double side = 1.0;
        double bowFactor = 1.0;
        double pace = 1.0;
        ulong seed = coordinateSeed;
        if (shape.Seed is { } seeded)
        {
            seed = Mix(seeded, shape.MoveIndex);
            side = UnitHash(seed, SideSalt) < 0.5 ? -1.0 : 1.0;
            bowFactor = 0.7 + 0.6 * UnitHash(seed, BowSalt);
            pace = 0.9 + 0.2 * UnitHash(seed, PaceSalt);
        }

        return new HandPath
        {
            Start = start,
            Destination = destination,
            Bow = side * bowFactor * shape.BowScale * Math.Min(BowRatio * distance, MaxBow),
            Seed = seed,
            Reach = 1.0 + shape.Overshoot,
            TremorPixels = shape.TremorPixels,
            Pace = pace,
        };
    }

    public Point DestinationPoint() => Destination;

    public ulong DurationMs((ulong MinMs, ulong MaxMs) travel)
    {
        double distance = DistanceBetween(Start, Destination);
        if (distance < 1.5)
        {
            return 0;
        }

        double fittsMin = CursorMotionProfile.DefaultTravelMinMs;
        double fittsMax = CursorMotionProfile.DefaultTravelMaxMs;
        double fitts = Math.Clamp(40.0 + 70.0 * Math.Log2(distance / 40.0 + 1.0), fittsMin, fittsMax);
        double min = travel.MinMs;
        double max = travel.MaxMs;
        double mapped = travel == (CursorMotionProfile.DefaultTravelMinMs, CursorMotionProfile.DefaultTravelMaxMs)
            ? fitts
            : min + (fitts - fittsMin) / (fittsMax - fittsMin) * (max - min);
        return (ulong)Math.Clamp(mapped * Pace, min, max);
    }

    public Point At(double t)
    {
        double dx = Destination.X - Start.X;
        double dy = Destination.Y - Start.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double travelled = Submovements(t);
        double normalX = 0.0;
        double normalY = 0.0;
        if (distance > double.Epsilon)
        {
            normalX = -dy / distance;
            normalY = dx / distance;
        }

        double settle = Math.Sin(Math.PI * t);
        double sideways = Bow * Math.Sin(Math.PI * Math.Pow(t, 0.8))
            + TremorPixels * Tremor(Seed, t) * settle;
        return new Point(Start.X + dx * travelled + normalX * sideways, Start.Y + dy * travelled + normalY * sideways);
    }

    private double Submovements(double t)
    {
        double primary = MinimumJerk(Math.Clamp(t / PrimarySpan, 0.0, 1.0));
        double correction = MinimumJerk(Math.Clamp((t - CorrectionStart) / (1.0 - CorrectionStart), 0.0, 1.0));
        return Reach * primary + (1.0 - Reach) * correction;
    }

    private static double DistanceBetween(Point start, Point destination) =>
        Math.Sqrt((destination.X - start.X) * (destination.X - start.X)
            + (destination.Y - start.Y) * (destination.Y - start.Y));

    internal static double MinimumJerk(double t) => t * t * t * (10.0 + t * (-15.0 + 6.0 * t));

    private static double Tremor(ulong seed, double t)
    {
        double x = t * TremorCycles;
        double step = Math.Floor(x);
        double fraction = x - step;
        double low = UnitHash(seed, (ulong)step);
        double high = UnitHash(seed, (ulong)(step + 1.0));
        double blend = fraction * fraction * (3.0 - 2.0 * fraction);
        return (low + (high - low) * blend) * 2.0 - 1.0;
    }

    private static double UnitHash(ulong seed, ulong index) =>
        (double)(Mix(seed, index) >> 11) / (double)(1UL << 53);

    private static ulong Mix(ulong seed, ulong index)
    {
        ulong hash = seed ^ (index * 0x9E37_79B9_7F4A_7C15);
        hash ^= hash >> 33;
        hash *= 0xFF51_AFD7_ED55_8CCD;
        return hash ^ (hash >> 33);
    }

    private static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (64 - count));

    private static ulong Bit(double value) => (ulong)BitConverter.DoubleToInt64Bits(value);
}
