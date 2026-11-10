using System;
using System.Text.Json.Serialization;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// One thing to show: where the cursor goes, whether it drags from a start
/// point, the label that rides with it, and the phase that decides its reveal.
/// </summary>
public sealed record CursorOverlayInstruction
{
    /// <summary>Where the cursor lands.</summary>
    [property: JsonPropertyName("destination")]
    public required Point Destination { get; set; }

    /// <summary>Where a drag starts, when the cursor carries a press.</summary>
    [property: JsonPropertyName("drag_from")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Point? DragFrom { get; set; }

    /// <summary>The label that rides with this presentation.</summary>
    [property: JsonPropertyName("label")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary>Whether the cursor presses at the destination.</summary>
    [property: JsonPropertyName("click")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsClick { get; set; }

    /// <summary>The element the cursor is on, which a caret follow moves from.</summary>
    [property: JsonPropertyName("target")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Rect? Target { get; set; }

    /// <summary>What the cursor is doing, which decides the reveal.</summary>
    [property: JsonPropertyName("phase")]
    [property: JsonConverter(typeof(CursorPhaseConverter))]
    public CursorPhase Phase { get; set; } = CursorPhase.Travel;

    /// <summary>Whether the cursor arrives showing the pointer image, on a pressable control.</summary>
    [property: JsonPropertyName("pointer")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPointer { get; set; }

    /// <summary>
    /// The instruction for a cursor that lands on <paramref name="destination"/>,
    /// carrying the config's greeting as its label. Throws when the destination
    /// is out of range, or the overlay is not enabled.
    /// </summary>
    public static CursorOverlayInstruction New(Point destination, CursorOverlayConfig config, bool click)
    {
        destination.Validate();
        if (config.IsDisabled)
            throw AdapterError.InvalidArgs("Agent cursor instruction requires enabled presentation").Unsent();

        return new CursorOverlayInstruction
        {
            Destination = destination,
            Label = config.Label,
            IsClick = click,
            Phase = CursorPhase.Travel,
        };
    }

    /// <summary>The instruction with its phase set.</summary>
    public CursorOverlayInstruction WithPhase(CursorPhase phase) => this with { Phase = phase };

    /// <summary>The instruction that arrives showing the pointer image.</summary>
    public CursorOverlayInstruction WithPointer(bool pointer) => this with { IsPointer = pointer };

    /// <summary>The instruction that drags from <paramref name="drag_from"/>.</summary>
    public CursorOverlayInstruction WithDragFrom(Point? drag_from) => this with { DragFrom = drag_from };

    /// <summary>
    /// The instruction that carries its target. A target that fails validation,
    /// or has no width or height, is dropped.
    /// </summary>
    public CursorOverlayInstruction WithTarget(Rect? target) => this with { Target = KeepTarget(target) };

    /// <summary>
    /// Whether this instruction sets the cursor off from <paramref name="at"/>
    /// for somewhere else: a travel, or a drag from its origin, to a point
    /// <see cref="CursorLayout.SamePoint"/> or more away. The agent's caret marks
    /// the field the cursor was typing in, so renderers clear it then and keep
    /// it for a travel that stays put, such as typing again into the same field.
    /// </summary>
    public bool DepartsFrom(Point? at)
    {
        var to = Phase switch
        {
            CursorPhase.Travel => Destination,
            CursorPhase.Drag => DragFrom ?? Destination,
            _ => null,
        };

        if (to is null)
            return false;

        if (at is null)
            return true;

        var dx = to.X - at.X;
        var dy = to.Y - at.Y;
        return Math.Sqrt(dx * dx + dy * dy) >= CursorLayout.SamePoint;
    }

    /// <summary>
    /// Rejects a destination or a drag origin that is out of range.
    /// </summary>
    public void Validate()
    {
        Destination.Validate();
        if (DragFrom is { } drag_from)
            drag_from.Validate();
    }

    private static Rect? KeepTarget(Rect? target)
    {
        if (target is not { } rect)
            return null;

        return rect.IsFiniteBounded() && rect.Width > 0.0 && rect.Height > 0.0 ? rect : null;
    }
}
