using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// The cursor label's pill: a black, fully rounded tag that fits its text. Each renderer measures the label in its own
/// system font and takes every other dimension from here, so the macOS and Windows overlays draw the same pill, with the
/// text centred in it both ways.
/// </summary>
public readonly record struct LabelPill
{
    /// <summary>The label's type size at the default cursor size.</summary>
    public const double LabelFontPoints = 12.5;

    /// <summary>The pill's height; its corner radius is half of it.</summary>
    public const double LabelPillHeight = 24.0;

    /// <summary>Space between the text and each end of the pill.</summary>
    public const double LabelPillPadding = 10.0;

    /// <summary>The widest the pill grows; longer text is cut short with an ellipsis.</summary>
    public const double LabelPillMaxWidth = 360.0;

    /// <summary>The pill's shadow: black at this opacity, offset down and blurred.</summary>
    public const double LabelShadowOpacity = 0.31;

    /// <summary>The pill's shadow offset, in points.</summary>
    public const double LabelShadowOffset = 2.0;

    /// <summary>The pill's shadow blur radius, in points.</summary>
    public const double LabelShadowBlur = 2.5;

    private readonly double width;
    private readonly double height;
    private readonly double textWidth;

    private LabelPill(double width, double height, double textWidth)
    {
        this.width = width;
        this.height = height;
        this.textWidth = textWidth;
    }

    /// <summary>The pill's width.</summary>
    public double Width => width;

    /// <summary>The pill's height.</summary>
    public double Height => height;

    /// <summary>The pill's size as a (width, height) pair.</summary>
    public (double Width, double Height) Size => (width, height);

    /// <summary>The pill's corner radius: half its height.</summary>
    public double CornerRadius => height / 2.0;

    /// <summary>
    /// The pill for <paramref name="label"/>, whose text measures <paramref name="textWidth"/> when set at
    /// <see cref="LabelFontPoints"/> times <paramref name="scale"/>. Null when the label says nothing, so an empty label
    /// shows no pill.
    /// </summary>
    public static LabelPill? ForLabel(string label, double textWidth, double scale)
    {
        if (string.IsNullOrWhiteSpace(label)
            || !double.IsFinite(textWidth)
            || !double.IsFinite(scale)
            || scale <= 0.0)
        {
            return null;
        }

        var padding = LabelPillPadding * scale;
        var widestText = LabelPillMaxWidth * scale - padding * 2.0;
        var clampedText = Math.Clamp(textWidth, 0.0, widestText);
        return new LabelPill(clampedText + padding * 2.0, LabelPillHeight * scale, clampedText);
    }

    /// <summary>
    /// The pill cut down to at most <paramref name="width"/>, as placing it on a narrow screen does. It keeps its
    /// padding, so the text gets what is left and is cut short with an ellipsis rather than running past the pill's ends.
    /// </summary>
    public LabelPill Within(double width)
    {
        if (double.IsNaN(width) || width >= this.width)
        {
            return this;
        }

        var padding = (this.width - textWidth) / 2.0;
        var clamped = Math.Max(width, padding * 2.0);
        return new LabelPill(clamped, height, clamped - padding * 2.0);
    }

    /// <summary>
    /// Where a line of text <paramref name="lineHeight"/> tall sits inside the pill, relative to its top-left corner:
    /// centred across and down.
    /// </summary>
    public Rect TextFrame(double lineHeight) =>
        new(
            (width - textWidth) / 2.0,
            (height - lineHeight) / 2.0,
            textWidth,
            lineHeight);
}
