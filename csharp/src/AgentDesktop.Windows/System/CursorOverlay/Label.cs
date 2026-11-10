using AgentDesktop.Core.CursorOverlay;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// What the card is allowed to say, bounded where an unvetted sender can reach it.
/// A label arriving over the pipe has passed only the transport's frame cap, which is thousands of bytes. The
/// configuration's own ceilings are applied by the process that builds the control, and any process on this desktop
/// running as this user can build one, so a caption long enough to cover the screen is reachable without touching the
/// configuration at all. The ceilings are therefore applied again on the reading side.
/// The ceilings come from core, so the reading side and the sending side cannot drift to two different numbers: a
/// renderer holding its own copy would keep cutting at the old ceiling after the configuration's moved, and nothing
/// would report the disagreement.
/// They are applied here rather than in the control's own validation because a label reaches that validation from the
/// action path too, on every platform. Refusing an over-long caption there would fail an enable that a renderer could
/// have drawn perfectly well once it was cut down, which is a rendering decision answered with an error.
/// </summary>
internal static class Label
{
    /// <summary>
    /// The label as the card may show it, or <c>null</c> when it says nothing.
    /// The ellipsis marks the cut for the same reason the configuration's does: a caption that simply stops reads as a
    /// caption the agent wrote badly.
    /// </summary>
    internal static string? Clamp(string label)
    {
        var words = WithinByteCeiling(label.Trim())
            .Split((char[]?)null, global::System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        if (words.Length <= CursorOverlayLimits.MaxLabelWords)
        {
            return string.Join(" ", words);
        }

        return string.Join(" ", words, 0, CursorOverlayLimits.MaxLabelWords) + "\u2026";
    }

    /// <summary>
    /// Cuts at the last character boundary at or before the byte ceiling. Cutting on the byte alone would split a
    /// multi-byte character and corrupt a caption the renderer was only ever asked to shorten.
    /// </summary>
    private static string WithinByteCeiling(string value)
    {
        var bytes = global::System.Text.Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= CursorOverlayLimits.MaxLabelBytes)
        {
            return value;
        }

        var end = CursorOverlayLimits.MaxLabelBytes;
        while (end > 0 && (bytes[end] & 0xC0) == 0x80)
        {
            end--;
        }

        return global::System.Text.Encoding.UTF8.GetString(bytes, 0, end);
    }
}
