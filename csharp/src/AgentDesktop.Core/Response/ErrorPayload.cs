namespace AgentDesktop.Core.Response;

/// <summary>The error half of the envelope: code, message, the optional suggestion and recovery
/// hint, the platform's own detail, and what the command established about delivery.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial class ErrorPayload
{
    public string Code { get; init; } = "";

    public string Message { get; init; } = "";

    public string? Suggestion { get; init; }

    public RecoveryHint? Recovery { get; init; }

    public string? PlatformDetail { get; init; }

    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.RawText)]
    public string? Details { get; init; }

    public DeliverySemantics Disposition { get; init; } = DeliverySemantics.Unknown();
}
