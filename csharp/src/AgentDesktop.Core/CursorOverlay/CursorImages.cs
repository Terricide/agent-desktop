using System.Text.Json.Serialization;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// Custom cursor artwork. The arrow is drawn while the cursor travels and
/// rests, the pointer once it arrives on a pressable control. A missing arrow
/// falls back to the built-in cursor; a missing pointer keeps the arrow.
/// </summary>
public sealed record CursorImages
{
    /// <summary>The image drawn while the cursor travels and rests.</summary>
    [property: JsonPropertyName("arrow")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CursorImage? Arrow { get; set; }

    /// <summary>The image drawn when the cursor arrives on a pressable control.</summary>
    [property: JsonPropertyName("pointer")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CursorImage? Pointer { get; set; }

    /// <summary>Whether the style carries no artwork, which the wire omits.</summary>
    public bool IsEmpty => Arrow is null && Pointer is null;

    /// <summary>
    /// The images with each present image validated. Throws when a present
    /// image cannot be drawn.
    /// </summary>
    public CursorImages Validated() => new()
    {
        Arrow = Arrow?.Validated(),
        Pointer = Pointer?.Validated(),
    };

    /// <summary>Sets the arrow artwork.</summary>
    public void SetArrow(CursorImage? arrow) => Arrow = arrow;

    /// <summary>Sets the pointer artwork.</summary>
    public void SetPointer(CursorImage? pointer) => Pointer = pointer;
}
