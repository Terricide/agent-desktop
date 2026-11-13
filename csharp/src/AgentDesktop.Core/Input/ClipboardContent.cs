namespace AgentDesktop.Core.Input;

/// <summary>Clipboard data in one representation.</summary>
public sealed record ClipboardContent
{
    public ClipboardFormat Format { get; init; }
    public string? Text { get; init; }
    public byte[]? Bytes { get; init; }
    public string? MimeType { get; init; }
}
