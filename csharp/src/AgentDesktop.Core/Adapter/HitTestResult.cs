using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Adapter;

/// <summary>
/// Classifies whether a hit-tested point reaches the intended target. A hit on the target itself or
/// one of its descendants reaches it; a hit outside the target's ancestor chain names a real occluder
/// (modal, overlay, sibling); a hit on the target's own ancestor is <c>Unknown</c> rather than a false
/// occlusion, since composited or custom-drawn views often expose no distinct child node to hit-test.
/// Probe failures are <c>Unknown</c> for the same reason: unavailable evidence is never a false failure.
/// </summary>
[EnvelopeSerialize]
public sealed partial record HitTestResult
{
    /// <summary>Which variant of the hit-test classification this value is.</summary>
    public enum HitTestOutcome
    {
        ReachesTarget,
        InterceptedBy,
        Unknown,
    }

    private HitTestResult(HitTestOutcome outcome, string? role, string? name, Rect? bounds)
    {
        Outcome = outcome;
        Role = role;
        Name = name;
        Bounds = bounds;
    }

    public HitTestOutcome Outcome { get; }

    public string? Role { get; }

    public string? Name { get; }

    public Rect? Bounds { get; }

    public static HitTestResult ReachesTarget { get; } = new(HitTestOutcome.ReachesTarget, null, null, null);

    public static HitTestResult Unknown { get; } = new(HitTestOutcome.Unknown, null, null, null);

    /// <summary>A named occluder. Role, name and bounds are each optional; absent evidence is omitted, never invented.</summary>
    public static HitTestResult InterceptedBy(string? role, string? name, Rect? bounds) =>
        new(HitTestOutcome.InterceptedBy, role, name, bounds);
}
