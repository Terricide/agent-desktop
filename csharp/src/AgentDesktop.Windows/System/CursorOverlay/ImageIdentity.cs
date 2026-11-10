using System.Linq;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Whether an image on disk is this tool's own binary.
/// </summary>
/// <remarks>
/// Two guards have to agree on that answer. The spawn guard refuses to fork a renderer from anything but the CLI's own
/// image, and a client refuses to hand controls to a pipe server that is not one. A stem that counted for the one and
/// not the other would either fork a renderer out of a host process or feed cursor coordinates and label text to a
/// stranger that can answer the acknowledgement byte, so both read their answer here.
///
/// The comparison is against the stem alone, never against the caller's own executable path: an FFI host legitimately
/// runs under another image and must still be able to reach the renderer it started.
/// </remarks>
internal static class ImageIdentity
{
    /// <summary>
    /// The file stem every image of this tool carries, with or without <c>.exe</c>.
    /// </summary>
    internal const string ImageStem = "agent-desktop";

    private static readonly char[] Separators = { '\\', '/' };

    /// <summary>
    /// Windows paths are case-insensitive, so <c>AGENT-DESKTOP.EXE</c> names the same image as <c>agent-desktop.exe</c>
    /// and has to pass the same guards. Only the final component counts: a build tree whose directories are named after
    /// the project is not itself the project's binary.
    /// </summary>
    internal static bool IsAgentDesktopImage(string path)
    {
        var stem = ImageStemOf(path);
        return stem is not null && EqualsAsciiIgnoreCase(stem, ImageStem);
    }

    /// <summary>
    /// The stem a refusal names, so a reader is told what was found and not only what was wanted. Null when the path has
    /// no file name, or when the name is not well-formed UTF-16.
    /// </summary>
    internal static string? ImageStemOf(string path)
    {
        var name = FinalComponent(path);
        if (name is null || HasUnpairedSurrogate(name))
        {
            return null;
        }

        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name.Substring(0, dot);
    }

    private static string? FinalComponent(string path)
    {
        var body = HasDrivePrefix(path) ? path.Substring(2) : path;
        var segments = body.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment != ".")
            .ToArray();
        var prefixLength = IsUncPrefix(body) ? 2 : 0;
        if (segments.Length <= prefixLength)
        {
            return null;
        }

        var last = segments[segments.Length - 1];
        return last == ".." ? null : last;
    }

    private static bool IsUncPrefix(string body)
    {
        return body.Length >= 2 && IsSeparator(body[0]) && IsSeparator(body[1]);
    }

    private static bool IsSeparator(char value)
    {
        return value == '\\' || value == '/';
    }

    private static bool HasDrivePrefix(string path)
    {
        return path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';
    }

    private static bool EqualsAsciiIgnoreCase(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (FoldAscii(left[index]) != FoldAscii(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static char FoldAscii(char value)
    {
        return value >= 'A' && value <= 'Z' ? (char)(value + ('a' - 'A')) : value;
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    index++;
                    continue;
                }

                return true;
            }

            if (char.IsLowSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}
