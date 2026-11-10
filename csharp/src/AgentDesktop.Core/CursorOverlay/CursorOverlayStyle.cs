using System;
using System.Linq;
using System.Text.Json.Serialization;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// How the drawn agent cursor looks: its colours, its size, the effects that
/// follow it, and the artwork that replaces the built-in dart.
/// </summary>
public sealed record CursorOverlayStyle
{
    private const string DefaultRim = "#111318";
    private const string DefaultAccent = "#4299FF";
    private const double MinSize = 0.5;
    private const double MaxSize = 4.0;

    /// <summary>
    /// The cursor body's colour. Without one the body takes the accent, so a
    /// single accent colour colours the whole cursor.
    /// </summary>
    [property: JsonPropertyName("fill")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fill { get; set; }

    /// <summary>The cursor outline's colour.</summary>
    [property: JsonPropertyName("rim")]
    public string Rim { get; set; } = DefaultRim;

    /// <summary>The colour that marks the cursor's arrival and its effects.</summary>
    [property: JsonPropertyName("accent")]
    public string Accent { get; set; } = DefaultAccent;

    /// <summary>The cursor's scale, relative to the built-in dart.</summary>
    [property: JsonPropertyName("size")]
    public double Scale { get; set; } = 1.0;

    /// <summary>Whether the arrival ripple is drawn.</summary>
    [property: JsonPropertyName("ripple")]
    public bool IsRipple { get; set; } = true;

    /// <summary>Whether the outline around a clicked element is drawn.</summary>
    [property: JsonPropertyName("highlight")]
    public bool IsHighlight { get; set; } = true;

    /// <summary>The artwork that replaces the built-in dart.</summary>
    [property: JsonPropertyName("images")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CursorImages Images { get; set; } = new();

    /// <summary>The cursor's scale.</summary>
    public double Size() => Scale;

    /// <summary>Whether the arrival ripple is drawn.</summary>
    public bool Ripple() => IsRipple;

    /// <summary>Whether the outline around a clicked element is drawn.</summary>
    public bool Highlight() => IsHighlight;

    /// <summary>The arrow artwork, or null when the built-in dart is drawn.</summary>
    public CursorImage? Image() => Images.Arrow;

    /// <summary>The pointer artwork, or null when the arrow is kept on arrival.</summary>
    public CursorImage? PointerImage() => Images.Pointer;

    /// <summary>
    /// The body colour: the fill when one is set and parses, otherwise the
    /// accent.
    /// </summary>
    public double[] FillRgb() => Rgb(Fill) ?? AccentRgb();

    /// <summary>The outline colour, with the shipped default when it does not parse.</summary>
    public double[] RimRgb() => Rgb(Rim) ?? [0.07, 0.07, 0.09];

    /// <summary>The accent colour, with the shipped default when it does not parse.</summary>
    public double[] AccentRgb() => Rgb(Accent) ?? [0.26, 0.60, 1.0];

    /// <summary>
    /// A copy that is a new instance, with its own artwork, so a caller can
    /// change one style without changing the style it came from. C# forbids a
    /// member named Clone on a record, so the copy is named here.
    /// </summary>
    public CursorOverlayStyle Copy() => new()
    {
        Fill = Fill,
        Rim = Rim,
        Accent = Accent,
        Scale = Scale,
        IsRipple = IsRipple,
        IsHighlight = IsHighlight,
        Images = Images with { },
    };

    /// <summary>
    /// The style that can be drawn: every colour a hex value, the size inside
    /// its range, and every present image validated. Throws when the style
    /// cannot be drawn.
    /// </summary>
    public CursorOverlayStyle Validated()
    {
        CheckColor(Fill);
        CheckColor(Rim);
        CheckColor(Accent);

        if (Scale < MinSize || Scale > MaxSize)
            throw AdapterError.InvalidArgs($"Cursor overlay size must be between {MinSize} and {MaxSize}").Unsent();

        return this with { Images = Images.Validated() };
    }

    /// <summary>Sets the body colour.</summary>
    public void SetFill(string fill) => Fill = fill;

    /// <summary>Sets the outline colour.</summary>
    public void SetRim(string rim) => Rim = rim;

    /// <summary>Sets the accent colour.</summary>
    public void SetAccent(string accent) => Accent = accent;

    /// <summary>Sets the cursor's scale.</summary>
    public void SetSize(double size) => Scale = size;

    /// <summary>Sets which effects follow the cursor.</summary>
    public void SetEffects(bool ripple, bool highlight)
    {
        IsRipple = ripple;
        IsHighlight = highlight;
    }

    /// <summary>Sets the arrow artwork.</summary>
    public void SetImage(CursorImage? image) => Images.SetArrow(image);

    /// <summary>Sets the pointer artwork.</summary>
    public void SetPointerImage(CursorImage? image) => Images.SetPointer(image);

    private static void CheckColor(string? color)
    {
        if (color is not null && Rgb(color) is null)
            throw AdapterError.InvalidArgs($"Cursor overlay color '{color}' must be a hex value like #FFFFFF").Unsent();
    }

    private static double[]? Rgb(string? value)
    {
        if (value is null)
            return null;

        var digits = value.StartsWith('#') ? value[1..] : value;
        var expanded = digits.Length switch
        {
            3 => string.Concat(digits.Select(c => new string(c, 2))),
            6 => digits,
            _ => null,
        };

        if (expanded is null)
            return null;

        var rgb = new double[3];
        for (var index = 0; index < 3; index++)
        {
            var channel = HexByte(expanded, index * 2);
            if (channel is null)
                return null;
            rgb[index] = channel.Value / 255.0;
        }

        return rgb;
    }

    private static byte? HexByte(string text, int at)
    {
        var high = Digit(text[at]);
        var low = Digit(text[at + 1]);
        return high is null || low is null ? null : (byte)((high.Value << 4) + low.Value);
    }

    private static int? Digit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => null,
    };
}
