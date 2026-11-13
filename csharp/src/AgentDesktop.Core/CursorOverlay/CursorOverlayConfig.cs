using System.Linq;
using System.Text.Json.Serialization;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// Whether the agent cursor overlay is on, the greeting its label carries, the
/// word limit that shortens long labels, the style it draws with, and the motion
/// profile it travels by.
/// </summary>
public sealed record CursorOverlayConfig
{
    /// <summary>Whether the overlay is drawn.</summary>
    [property: JsonPropertyName("enabled")]
    public bool IsEnabled { get; set; }

    /// <summary>The greeting the label starts with.</summary>
    [property: JsonPropertyName("label")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    /// <summary>The most words a label keeps before it is shortened.</summary>
    [property: JsonPropertyName("max_words")]
    public int MaxWords { get; set; } = CursorOverlayLimits.DefaultLabelWords;

    /// <summary>The style the overlay draws with.</summary>
    [property: JsonPropertyName("style")]
    public CursorOverlayStyle Style { get; set; } = new();

    /// <summary>Whether each agent on the session keeps its own overlay.</summary>
    [property: JsonPropertyName("multi_agent")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsMultiAgent { get; set; }

    /// <summary>The motion profile the cursor travels by.</summary>
    [property: JsonPropertyName("motion")]
    public CursorMotionProfile Motion { get; set; } = new();

    /// <summary>Whether the overlay is off.</summary>
    public bool IsDisabled => !IsEnabled;

    /// <summary>
    /// The enabled config: the greeting trimmed and shortened to the word
    /// limit, the style validated, and the motion validated. Throws when the
    /// word limit is out of range, the greeting is too long, or the style or
    /// motion cannot be drawn.
    /// </summary>
    public static CursorOverlayConfig Enabled(string? label, int max_words) =>
        new CursorOverlayConfig { IsEnabled = true, Label = label, MaxWords = max_words }.Validated();

    /// <summary>
    /// The config with its word limit inside the ceiling, its greeting trimmed
    /// and shortened to that limit, its style validated, and its motion
    /// validated.
    /// </summary>
    public CursorOverlayConfig Validated()
    {
        if (MaxWords < 1 || MaxWords > CursorOverlayLimits.MaxLabelWords)
            throw AdapterError.InvalidArgs(
                $"Agent cursor label word limit must be between 1 and {CursorOverlayLimits.MaxLabelWords}").Unsent();

        if (!IsEnabled && Label is not null)
            throw AdapterError.InvalidArgs(
                "Agent cursor label requires the agent cursor overlay to be enabled").Unsent();

        if (Label is { } label && CursorImage.JsonEscapedLength(label.Trim()) > CursorOverlayLimits.MaxLabelBytes)
            throw AdapterError.InvalidArgs(
                $"Agent cursor label must be at most {CursorOverlayLimits.MaxLabelBytes} bytes once JSON-escaped").Unsent();

        return this with
        {
            Label = LimitWords(Label, MaxWords),
            Style = Style.Validated(),
            Motion = Motion.Validated(),
        };
    }

    /// <summary>The config that draws with <paramref name="style"/>.</summary>
    public CursorOverlayConfig WithStyle(CursorOverlayStyle style) => this with { Style = style.Validated() };

    /// <summary>The config that travels by <paramref name="motion"/>.</summary>
    public CursorOverlayConfig WithMotion(CursorMotionProfile motion) => this with { Motion = motion.Validated() };

    /// <summary>The config with per-agent overlays on or off.</summary>
    public CursorOverlayConfig WithMultiAgent(bool enabled) => this with { IsMultiAgent = enabled };

    /// <summary>
    /// The text shortened to <paramref name="max_words"/> words, joined with
    /// single spaces, with an ellipsis when words were cut. Null when the text
    /// says nothing.
    /// </summary>
    private static string? LimitWords(string? text, int max_words)
    {
        if (text is null)
            return null;

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return null;

        if (words.Length <= max_words)
            return string.Join(' ', words);

        return string.Join(' ', words.Take(max_words)) + '…';
    }
}
