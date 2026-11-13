using AgentDesktop.Core.Response;
using System.Text.Json.Nodes;

namespace AgentDesktop.Core.Response;

/// <summary>Builds the single JSON envelope every command prints, and writes it with the
/// generated field-order writer. Null fields are omitted.</summary>
public static class Envelope
{
    public const string Version = "2.6";

    public static EnvelopeResponse Ok(string command, JsonNode? data = null) =>
        new() { Ok = true, Command = command, Data = data };

    public static EnvelopeResponse Err(string command, AdapterError error) =>
        new() { Ok = false, Command = command, Error = Payload(error) };

    public static ErrorPayload Payload(AdapterError error) => new()
    {
        Code = error.Code.AsString(),
        Message = error.Message,
        Suggestion = error.Suggestion,
        Recovery = error.Recovery,
        PlatformDetail = error.PlatformDetail,
        Details = error.Details,
        Disposition = error.Disposition,
    };

    /// <summary>The envelope text: each field appended at its declared position.</summary>
    public static string Write(EnvelopeResponse response)
    {
        var writer = new EnvelopeWriter();
        response.WriteJson(writer);
        return writer.Text();
    }

    /// <summary>The exit code the process returns for an envelope: success, structured error, argument or parse error.</summary>
    public static int ExitCode(EnvelopeResponse response) => response.Ok ? ExitCodes.Success : ExitCodes.Error;
}

public static class ExitCodes
{
    public const int Success = 0;

    public const int Error = 1;

    public const int ParseError = 2;
}
