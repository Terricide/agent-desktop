using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// Reads and writes the wire names for cursor phases, which are the snake_case
/// names the Rust renderer uses. The enum members are C# names, so the wire
/// names cannot come from them.
/// </summary>
public sealed class CursorPhaseConverter : JsonConverter<CursorPhase>
{
    /// <summary>Reads one wire phase name.</summary>
    public override CursorPhase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var name = reader.GetString();
        return name switch
        {
            "travel" => CursorPhase.Travel,
            "drag" => CursorPhase.Drag,
            "effect" => CursorPhase.Effect,
            "caret" => CursorPhase.Caret,
            "caret_follow" => CursorPhase.CaretFollow,
            _ => throw new JsonException($"Unknown cursor phase {name}"),
        };
    }

    /// <summary>Writes one wire phase name.</summary>
    public override void Write(Utf8JsonWriter writer, CursorPhase value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(WireName(value));
    }

    private static string WireName(CursorPhase phase) => phase switch
    {
        CursorPhase.Travel => "travel",
        CursorPhase.Drag => "drag",
        CursorPhase.Effect => "effect",
        CursorPhase.Caret => "caret",
        CursorPhase.CaretFollow => "caret_follow",
        _ => throw new JsonException($"Unknown cursor phase {phase}"),
    };
}
