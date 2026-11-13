using System;
using System.Text.Json.Serialization;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// How the drawn agent cursor travels to its destination. This is presentation
/// only; physical input keeps its own motion.
/// </summary>
public sealed record CursorMotionProfile
{
    internal const ulong DefaultTravelMinMs = 90;
    internal const ulong DefaultTravelMaxMs = 320;
    private const double DefaultBow = 1.0;
    private const double DefaultOvershoot = 0.035;
    private const double DefaultTremor = 1.1;
    private const ulong MinTravelMs = 30;
    private const double MaxBow = 3.0;
    private const double MaxOvershoot = 0.15;
    private const double MaxTremor = 4.0;
    private const ulong MaxDwellMs = 300;

    /// <summary>The shortest travel the renderer may take for this profile.</summary>
    [property: JsonPropertyName("travel_min_ms")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ulong TravelMinMs { get; set; } = DefaultTravelMinMs;

    /// <summary>The longest travel the renderer may take for this profile.</summary>
    [property: JsonPropertyName("travel_max_ms")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ulong TravelMaxMs { get; set; } = DefaultTravelMaxMs;

    /// <summary>How far the travel arc bows away from the straight line.</summary>
    [property: JsonPropertyName("bow")]
    public double Bow { get; set; } = DefaultBow;

    /// <summary>How far the cursor passes the destination before it settles back.</summary>
    [property: JsonPropertyName("overshoot")]
    public double Overshoot { get; set; } = DefaultOvershoot;

    /// <summary>How much the travel jitters.</summary>
    [property: JsonPropertyName("tremor")]
    public double Tremor { get; set; } = DefaultTremor;

    /// <summary>How long the cursor holds at the destination before the action dispatches.</summary>
    [property: JsonPropertyName("dwell_ms")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ulong DwellMs { get; set; }

    /// <summary>The seed that makes one profile's jitter repeat, so a run can be replayed.</summary>
    [property: JsonPropertyName("seed")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ulong? Seed { get; set; }

    /// <summary>Whether the profile says nothing beyond the defaults, which the wire omits.</summary>
    public bool IsDefault() =>
        TravelMinMs == DefaultTravelMinMs
        && TravelMaxMs == DefaultTravelMaxMs
        && Bow == DefaultBow
        && Overshoot == DefaultOvershoot
        && Tremor == DefaultTremor
        && DwellMs == 0
        && Seed is null;

    /// <summary>
    /// Rejects a profile the renderer cannot travel by: a travel range that
    /// starts below the floor or ends before it begins, a bow, overshoot, or
    /// tremor past its ceiling, a dwell past its ceiling, or a travel plus dwell
    /// past the motion budget.
    /// </summary>
    public void Validate()
    {
        if (TravelMinMs < MinTravelMs || TravelMinMs > TravelMaxMs)
            throw AdapterError.InvalidArgs($"Cursor --travel-ms needs {MinTravelMs} <= MIN <= MAX").Unsent();

        CheckRange("--bow", Bow, MaxBow);
        CheckRange("--overshoot", Overshoot, MaxOvershoot);
        CheckRange("--tremor", Tremor, MaxTremor);

        if (DwellMs > MaxDwellMs)
            throw AdapterError.InvalidArgs($"Cursor --dwell-ms must be between 0 and {MaxDwellMs}").Unsent();

        if (TravelMaxMs + DwellMs > CursorOverlayTiming.MotionBudgetMs)
            throw AdapterError.InvalidArgs(
                $"Cursor travel max plus dwell must not exceed {CursorOverlayTiming.MotionBudgetMs} ms").Unsent();
    }

    /// <summary>The validated profile.</summary>
    public CursorMotionProfile Validated()
    {
        Validate();
        return this;
    }

    /// <summary>Sets the travel range the renderer picks within.</summary>
    public void SetTravelMs(ulong min, ulong max)
    {
        TravelMinMs = min;
        TravelMaxMs = max;
    }

    /// <summary>Sets how far the arc bows.</summary>
    public void SetBow(double bow) => Bow = bow;

    /// <summary>Sets how far the cursor passes its destination.</summary>
    public void SetOvershoot(double overshoot) => Overshoot = overshoot;

    /// <summary>Sets how much the travel jitters.</summary>
    public void SetTremor(double tremor) => Tremor = tremor;

    /// <summary>Sets how long the cursor holds at the destination.</summary>
    public void SetDwellMs(ulong dwell_ms) => DwellMs = dwell_ms;

    /// <summary>Sets the jitter seed.</summary>
    public void SetSeed(ulong? seed) => Seed = seed;

    /// <summary>The travel range the renderer picks within.</summary>
    public (ulong MinMs, ulong MaxMs) TravelMs() => (TravelMinMs, TravelMaxMs);

    private static void CheckRange(string flag, double value, double max)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > max)
            throw AdapterError.InvalidArgs($"Cursor {flag} must be between 0 and {max}").Unsent();
    }
}
