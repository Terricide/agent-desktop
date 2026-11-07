namespace AgentDesktop.Core.Response;

/// <summary>How one field joins the JSON a type writes: at its key, inside the parent
/// object with no key of its own, never, or only when it carries a value.</summary>
public enum EnvelopeFieldMode
{
    Standard,
    Flatten,
    Skip,
    SkipWhenFalse,
    RawJson,
    RawText,
}
