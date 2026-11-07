using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Tree;

/// <summary>A rectangle in screen coordinates. Coordinates are bounded and dimensions non-negative.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public readonly partial record struct Rect(double X, double Y, double Width, double Height)
{
    public const double MaxGeometryMagnitude = 10_000_000.0;

    public bool IsFiniteBounded() =>
        double.IsFinite(X) && double.IsFinite(Y)
        && Math.Abs(X) <= MaxGeometryMagnitude
        && Math.Abs(Y) <= MaxGeometryMagnitude
        && double.IsFinite(Width) && double.IsFinite(Height)
        && Width >= 0.0 && Width <= MaxGeometryMagnitude
        && Height >= 0.0 && Height <= MaxGeometryMagnitude;

    public Rect Validate()
    {
        if (IsFiniteBounded())
        {
            return this;
        }

        throw new AdapterError(
            ErrorCode.InvalidArgs,
            "Rectangle must contain finite bounded coordinates and non-negative bounded dimensions");
    }

    /// <summary>Canonical geometry hash at 0.01 precision; null for a rectangle that fails validation.</summary>
    public long? BoundsHash()
    {
        if (!IsFiniteBounded())
        {
            return null;
        }

        long hash = 0;
        foreach (double value in new[] { X, Y, Width, Height })
        {
            long canonical = (long)Math.Round(value * 100.0);
            hash = hash * 31 + canonical;
        }

        return hash;
    }
}
