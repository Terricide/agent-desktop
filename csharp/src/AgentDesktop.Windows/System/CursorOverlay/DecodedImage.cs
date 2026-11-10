namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// A decoded PNG: its size and its premultiplied ARGB pixels, row by row.
/// </summary>
internal sealed record DecodedImage(int Width, int Height, uint[] Pixels);
