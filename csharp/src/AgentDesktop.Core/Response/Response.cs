using AgentDesktop.Core.Response;
using System.Text.Json.Nodes;

namespace AgentDesktop.Core.Response;

/// <summary>The single JSON envelope every command prints. Null fields are omitted, and the
/// fields appear in the order declared here: the writer the source generator emits appends
/// them at their positions, with no runtime type model consulted.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial class EnvelopeResponse
{
    public string Version { get; init; } = Envelope.Version;

    public bool Ok { get; init; }

    public string Command { get; init; } = "";

    /// <summary>The command's own JSON object, built by the command with the same writer.</summary>
    [AgentDesktop.Core.Response.EnvelopeField(AgentDesktop.Core.Response.EnvelopeFieldMode.RawJson)]
    public JsonNode? Data { get; init; }

    public ErrorPayload? Error { get; init; }

    public string ToJsonString()
    {
        var writer = new EnvelopeWriter();
        WriteJson(writer);
        return writer.Text();
    }
}
