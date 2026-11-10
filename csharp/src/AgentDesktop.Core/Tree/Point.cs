using System;
using System.Text.Json.Serialization;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Tree;

/// <summary>
/// A point in screen coordinates.
/// </summary>
public sealed record Point(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y)
{
    private const double MaxCoordinate = 10_000_000.0;

    /// <summary>
    /// Rejects coordinates that are not finite, or that reach past the geometry
    /// ceiling the platform adapters accept. Returns the point unchanged.
    /// </summary>
    public Point Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) || Math.Abs(X) > MaxCoordinate || Math.Abs(Y) > MaxCoordinate)
            throw AdapterError.InvalidArgs("Point coordinates must be finite and within 10,000,000")
                .WithSuggestion("Use finite coordinates within 10,000,000.");

        return this;
    }
}
