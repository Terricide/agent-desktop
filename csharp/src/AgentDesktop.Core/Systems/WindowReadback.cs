using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Systems;

/// <summary>The read-back that verifies a window operation landed. A geometry read-back carries a tolerance and its units; a state read-back carries neither.</summary>
public readonly record struct WindowReadback
{
    public string Scope { get; }

    public double? Tolerance { get; }

    public string? Units { get; }

    private WindowReadback(string scope, double? tolerance, string? units)
    {
        Scope = scope;
        Tolerance = tolerance;
        Units = units;
    }

    /// <summary>A read-back that verifies window state, with no tolerance.</summary>
    public static WindowReadback State(string scope) => new(scope, null, null);

    /// <summary>A read-back that verifies window geometry within a tolerance in the given units.</summary>
    public static WindowReadback Geometry(string scope, double tolerance, string units) =>
        new(scope, tolerance, units);

    /// <summary>The verification details as JSON text, keys in sorted order as the envelope writes them.</summary>
    internal string DetailsJson()
    {
        var writer = new EnvelopeWriter();
        writer.ObjectStart();
        if (Tolerance is { } tolerance && Units is { } units)
        {
            writer.Key("tolerance");
            writer.Number(tolerance);
            writer.Key("units");
            writer.String(units);
        }

        writer.Key("verification_scope");
        writer.String(Scope);
        writer.ObjectEnd();
        return writer.Text();
    }
}
