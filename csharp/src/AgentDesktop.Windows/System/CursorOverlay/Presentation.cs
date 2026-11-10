using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// What one composed frame is made of, and whether the overlay has gone quiet. Held apart from the host so the host
/// file stays inside the size cap and so the two pieces of per-frame state can be read without the paint path around
/// them.
/// </summary>
/// <remarks>
/// The reveal instant belongs here rather than beside the rest state because it is a property of the label: it is set
/// when the label changes and read while the card is drawn.
/// </remarks>
internal sealed class Presentation
{
    internal CursorOverlayStyle Style { get; set; } = new();

    internal required Point Pose { get; set; }

    internal string? Label { get; set; }

    internal Reveal Reveal { get; set; } = Reveal.Settled;

    private bool Pointing { get; set; }

    private bool ArrivesPointing { get; set; }

    internal static Presentation RestingAt(Point pose)
    {
        return new Presentation
        {
            Pose = pose,
            Style = new CursorOverlayStyle(),
            Label = null,
            Reveal = Reveal.Settled,
            Pointing = false,
            ArrivesPointing = false,
        };
    }

    /// <summary>
    /// The label a control asks the card to show. The greeting <c>Enable</c> sends is left off while the cursor is
    /// drawn from a custom image.
    /// </summary>
    internal string? LabelFrom(CursorOverlayControl control)
    {
        var greetingOverImage = control.IsEnable && Style.Image() is not null;
        return greetingOverImage ? null : control.Label;
    }

    /// <summary>
    /// A travel sets off showing the arrow, and arrives showing the pointer image when it lands on a pressable control.
    /// </summary>
    internal void Depart(bool arrivesPointing)
    {
        Pointing = false;
        ArrivesPointing = arrivesPointing;
    }

    internal void Arrive()
    {
        Pointing = ArrivesPointing;
        ArrivesPointing = false;
    }

    /// <summary>
    /// The label's pill, sized to the text as it is set at this size, or nothing when there is no label or it cannot be
    /// measured.
    /// </summary>
    internal LabelPill? BuildLabelPill()
    {
        var label = Label;
        if (label is null)
        {
            return null;
        }

        var scale = Style.Size();
        var width = OverlayText.MeasureLabel(label, LabelPill.LabelFontPoints * scale);
        return width is { } measured ? LabelPill.ForLabel(label, measured, scale) : null;
    }

    /// <summary>
    /// The style a frame is drawn with: the session's own, with the pointer image standing in for the arrow while the
    /// cursor rests on a pressable control.
    /// </summary>
    internal CursorOverlayStyle ShownStyle()
    {
        var shown = Style.Copy();
        if (Pointing && Style.PointerImage() is { } pointer)
        {
            shown.SetImage(pointer);
        }

        return shown;
    }
}
