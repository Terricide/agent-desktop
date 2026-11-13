using System.Runtime.InteropServices;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>
/// Standard and registered Win32 clipboard format identifiers.
/// </summary>
internal static partial class ClipboardFormats
{
    internal const uint CfUnicodeText = 13;
    internal const uint CfDib = 8;
    internal const uint CfDibV5 = 17;
    internal const uint CfHdrop = 15;

    /// <summary>
    /// Registers the "PNG" clipboard format name with the system, returning its identifier,
    /// or null when the system refuses the registration.
    /// </summary>
    internal static uint? GetRegisteredPngFormat()
    {
        var format = RegisterClipboardFormatW("PNG");
        return format != 0 ? format : null;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string name);
}
