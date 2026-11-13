using System.Text.Json.Nodes;

namespace AgentDesktop.Core.Systems;

/// <summary>What a window write is verified against: the geometry it must settle within, or the
/// placement state it must reach. A geometry scope carries the re-read tolerance in pixels, because a
/// window can settle a few pixels away from the rectangle requested; a state scope carries none.
/// Port of <c>WindowReadback</c> in <c>crates/core/src/system/window_readback.rs</c>.</summary>
public sealed record WindowReadback
{
    public string Scope { get; init; } = "";
    public double? Tolerance { get; init; }
    public string? Units { get; init; }

    public static WindowReadback State(string scope) => new() { Scope = scope };

    public static WindowReadback Geometry(string scope, double tolerance, string units) =>
        new() { Scope = scope, Tolerance = tolerance, Units = units };

    /// <summary>The read-back as the report's details: the scope the adapter verified, and the
    /// tolerance a geometry scope is measured against.</summary>
    public JsonObject Details()
    {
        var details = new JsonObject { ["verification_scope"] = Scope };
        if (Tolerance is { } tolerance)
        {
            details["tolerance"] = tolerance;
            details["units"] = Units;
        }

        return details;
    }
}
