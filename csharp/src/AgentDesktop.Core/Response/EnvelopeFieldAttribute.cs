namespace AgentDesktop.Core.Response;

/// <summary>Says how one property joins the JSON its type writes, and under what name.</summary>
[System.AttributeUsage(System.AttributeTargets.Property)]
public sealed class EnvelopeFieldAttribute : System.Attribute
{
    public EnvelopeFieldMode Mode { get; }

    public string? Name { get; }

    public EnvelopeFieldAttribute(EnvelopeFieldMode mode = EnvelopeFieldMode.Standard, string? name = null)
    {
        Mode = mode;
        Name = name;
    }
}
