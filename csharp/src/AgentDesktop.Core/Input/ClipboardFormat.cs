namespace AgentDesktop.Core.Input;

/// <summary>The clipboard representation a read asks for. Auto takes whatever the pasteboard holds.</summary>
public enum ClipboardFormat
{
    Auto,
    Text,
    Image,
    Html,
    Rtf,
}
