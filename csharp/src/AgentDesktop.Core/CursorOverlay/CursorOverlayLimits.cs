namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// The ceilings the cursor overlay enforces on its text and on the image files
/// it is asked to draw.
/// </summary>
public static class CursorOverlayLimits
{
    /// The most words a label keeps before it is shortened with an ellipsis.
    public const int MaxLabelWords = 12;

    /// The word limit a label starts with when none is asked for.
    public const int DefaultLabelWords = 6;

    /// The most bytes a label, a greeting, or a presentation label may occupy
    /// once JSON-escaped.
    public const int MaxLabelBytes = 512;

    /// The most bytes an image path may occupy once JSON-escaped.
    public const int MaxPathBytes = 1024;

    /// The most a hotspot coordinate may be, in image pixels from the corner.
    public const double MaxHotspot = 512.0;

    /// The largest cursor image file the renderer will read, in bytes.
    public const long MaxImageBytes = 2 * 1024 * 1024;

    /// The largest PNG pixel width or height the renderer will decode.
    public const int MaxImagePixels = 1024;
}
