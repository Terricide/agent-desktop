using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System;

/// <summary>Resolves launch identifiers, budgets launch option text, and builds the environment
/// block a launch hands to CreateProcessW. Windows environment variable names are
/// case-insensitive, so keys are folded to ASCII uppercase only to detect a collision between a
/// caller override and an inherited entry; the spelling that lands in the block is the caller's
/// own for an override, and the inherited spelling otherwise.</summary>
internal static class LaunchPath
{
    private const int MaxArgumentCount = 256;
    private const int MaxEnvironmentCount = 256;
    private const int MaxLaunchTextBytes = 1024 * 1024;

    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>The image name a launch request names. An absolute path is taken as given, and a
    /// bare name without an extension names its .exe image.</summary>
    internal static string LaunchIdentifier(string id)
    {
        if (id.Length == 0 || id.Contains("..") || HasControlCharacter(id))
        {
            throw InvalidIdentifierError(id);
        }

        if (IsAbsoluteLaunchPath(id))
        {
            return id;
        }

        if (IsBareName(id))
        {
            return HasExtension(id) ? id : id + ".exe";
        }

        throw InvalidIdentifierError(id);
    }

    /// <summary>Refuses a launch whose argument or environment entry count exceeds the supported
    /// limit, or whose argument and environment text exceeds one MiB. The budget counts UTF-8
    /// bytes, the same width the launch hands to the platform.</summary>
    internal static void ValidateLaunchOptions(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string> env)
    {
        if (args.Count > MaxArgumentCount || env.Count > MaxEnvironmentCount)
        {
            throw AdapterError.FromCode(
                ErrorCode.InvalidArgs,
                "Launch argument or environment entry count exceeds the supported limit");
        }

        long textBytes = 0;
        foreach (var arg in args)
        {
            textBytes += Utf8ByteCount(arg);
        }

        foreach (var entry in env)
        {
            textBytes += Utf8ByteCount(entry.Key) + Utf8ByteCount(entry.Value);
        }

        if (textBytes > MaxLaunchTextBytes)
        {
            throw AdapterError.FromCode(
                ErrorCode.InvalidArgs,
                "Launch argument and environment data exceeds one MiB");
        }
    }

    /// <summary>Finds the executable a launch identifier names. An absolute path must be a file
    /// where it says, and a bare name is searched only under System32 then the Windows directory,
    /// so a name found nowhere is refused instead of left to a wider search path.</summary>
    internal static string ResolveExecutable(string id)
    {
        if (IsAbsoluteLaunchPath(id))
        {
            if (global::System.IO.File.Exists(id))
            {
                return id;
            }

            throw AdapterError
                .FromCode(ErrorCode.AppNotFound, "Launch path does not exist")
                .WithDetails(new JsonObject { ["app_name"] = id });
        }

        if (!IsBareName(id))
        {
            throw InvalidIdentifierError(id);
        }

        var (system32, windows) = SystemDirectories();
        foreach (var directory in new[] { system32, windows })
        {
            var candidate = JoinPath(directory, id);
            if (global::System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw AdapterError
            .FromCode(ErrorCode.AppNotFound, "Bare launch name is not present in the system directories")
            .WithDetails(new JsonObject { ["app_name"] = id })
            .WithSuggestion(
                "Pass an absolute path, or a bare name that exists under System32 or the Windows directory.");
    }

    /// <summary>The image name a launch identifier ends with, read from its last path separator.</summary>
    internal static string ImageFileName(string id)
    {
        var separator = id.LastIndexOfAny(Separators);
        return separator < 0 ? id : id[(separator + 1)..];
    }

    /// <summary>The null-delimited UTF-16 environment block for CreateProcessW, built from the
    /// inherited environment merged with the caller's overrides. A final null terminates the block
    /// the way the platform requires.</summary>
    internal static List<ushort> EnvironmentBlock(IReadOnlyDictionary<string, string> overrides)
    {
        var block = new List<ushort>();
        foreach (var (key, value) in MergeEnvironment(InheritedVariables(), overrides))
        {
            AppendUtf16(block, key + "=" + value);
            block.Add(0);
        }

        block.Add(0);
        return block;
    }

    /// <summary>Folds the inherited environment and caller overrides into one entry list. Inherited
    /// entries with malformed names - empty, or containing '=' or NUL, the hidden current-directory
    /// variables some Windows images carry in their process environment - are skipped rather than
    /// fatal: this process did not choose them, and one stray machine-level entry must not fail
    /// every launch on that machine. Caller overrides are held to the strict rule instead, because
    /// a rejected launch with InvalidArgs is the honest answer to input this tool was handed
    /// directly. Returned keys are folded to ASCII uppercase for collision detection; values keep
    /// their inherited spelling, and entries are ordered by the folded key.</summary>
    internal static List<(string Key, string Value)> MergeEnvironment(
        IEnumerable<(string Key, string Value)> inherited,
        IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new SortedDictionary<string, (string Key, string Value)>(global::System.StringComparer.Ordinal);
        foreach (var (key, value) in inherited)
        {
            if (key.Length == 0 || key.Contains('=') || key.Contains('\0'))
            {
                continue;
            }

            merged[FoldAsciiUpper(key)] = (key, value);
        }

        var orderedOverrides = new SortedDictionary<string, string>(global::System.StringComparer.Ordinal);
        foreach (var entry in overrides)
        {
            orderedOverrides[entry.Key] = entry.Value;
        }

        foreach (var entry in orderedOverrides)
        {
            merged[FoldAsciiUpper(entry.Key)] = (entry.Key, entry.Value);
        }

        var entries = new List<(string Key, string Value)>(merged.Values);
        foreach (var (key, value) in entries)
        {
            if (key.Contains('=') || key.Contains('\0') || value.Contains('\0'))
            {
                throw AdapterError.FromCode(
                    ErrorCode.InvalidArgs,
                    "Launch environment entries must not contain NUL or '=' in the key");
            }
        }

        return entries;
    }

    /// <summary>The block a launch actually hands to CreateProcessW: null only when nothing needs
    /// to change from this process's own environment - no caller override, and this process itself
    /// carries no entry for stripKey to remove. Any other case builds an explicit block with
    /// stripKey scrubbed unconditionally, so a value this process only holds because it was handed
    /// down to it (an adopted lease handle, say) never reaches a process this launch starts,
    /// whether or not the caller supplied overrides of its own.</summary>
    internal static List<ushort>? ChildEnvironmentBlock(
        IReadOnlyDictionary<string, string> overrides,
        string stripKey)
    {
        if (overrides.Count == 0 && global::System.Environment.GetEnvironmentVariable(stripKey) == null)
        {
            return null;
        }

        return StripEnvironmentEntry(EnvironmentBlock(overrides), stripKey);
    }

    /// <summary>Removes any entry named key from an already-encoded CreateProcessW environment
    /// block, comparing the name case-insensitively the way Windows resolves environment variable
    /// names. Round-trips through text rather than walking the UTF-16 units directly: every entry
    /// in a block this module built started as valid text with no interior NUL, so the decode is
    /// exact, not lossy in practice.</summary>
    private static List<ushort> StripEnvironmentEntry(List<ushort> block, string key)
    {
        var foldedPrefix = FoldAsciiUpper(key) + "=";
        var stripped = new List<ushort>(block.Count);
        foreach (var entry in DecodeUtf16(block).Split('\0'))
        {
            if (entry.Length == 0
                || FoldAsciiUpper(entry).StartsWith(foldedPrefix, global::System.StringComparison.Ordinal))
            {
                continue;
            }

            AppendUtf16(stripped, entry);
            stripped.Add(0);
        }

        stripped.Add(0);
        return stripped;
    }

    private static (string System32, string Windows) SystemDirectories()
    {
        var root = global::System.Environment.GetEnvironmentVariable("SystemRoot")
            ?? global::System.Environment.GetEnvironmentVariable("WINDIR");
        if (root == null)
        {
            throw AdapterError.FromCode(ErrorCode.Internal, "SystemRoot is unset");
        }

        return (JoinPath(root, "System32"), root);
    }

    private static bool IsAbsoluteLaunchPath(string id)
    {
        if (id.StartsWith("\\\\?\\", global::System.StringComparison.Ordinal)
            || id.StartsWith("\\\\", global::System.StringComparison.Ordinal))
        {
            return true;
        }

        return id.Length >= 3 && IsAsciiLetter(id[0]) && id[1] == ':' && id[2] == '\\';
    }

    private static bool IsBareName(string id)
    {
        if (id.Length == 0 || id.Contains('\\') || id.Contains('/'))
        {
            return false;
        }

        return !(id.Length >= 2 && IsAsciiLetter(id[0]) && id[1] == ':');
    }

    private static bool HasExtension(string name) => name.LastIndexOf('.') > 0;

    private static bool HasControlCharacter(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (global::System.Char.IsControl(text[index]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAsciiLetter(char value) =>
        (value >= 'a' && value <= 'z') || (value >= 'A' && value <= 'Z');

    private static int Utf8ByteCount(string text) =>
        global::System.Text.Encoding.UTF8.GetByteCount(text);

    private static string JoinPath(string directory, string name) =>
        global::System.IO.Path.Combine(directory, name);

    private static void AppendUtf16(List<ushort> block, string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            block.Add(text[index]);
        }
    }

    private static string DecodeUtf16(List<ushort> block)
    {
        var units = new char[block.Count];
        for (var index = 0; index < block.Count; index++)
        {
            units[index] = (char)block[index];
        }

        return new string(units);
    }

    /// <summary>Folds ASCII letters to uppercase and leaves every other code point as written, so a
    /// non-ASCII key is never folded into a collision it does not have on Windows.</summary>
    private static string FoldAsciiUpper(string text)
    {
        var folded = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            folded[index] = text[index] >= 'a' && text[index] <= 'z'
                ? (char)(text[index] - 32)
                : text[index];
        }

        return new string(folded);
    }

    private static AdapterError InvalidIdentifierError(string id) =>
        AdapterError
            .FromCode(
                ErrorCode.InvalidArgs,
                "Invalid app identifier: use an absolute path or a bare executable name")
            .WithDetails(new JsonObject { ["app_name"] = id })
            .WithSuggestion(
                "Use a full path (drive + backslash, UNC, or \\\\?\\) or a bare name resolved only under System32 / Windows.");

    private static IEnumerable<(string Key, string Value)> InheritedVariables()
    {
        foreach (var entry in global::System.Environment.GetEnvironmentVariables())
        {
            if (entry is global::System.Collections.DictionaryEntry pair)
            {
                yield return (pair.Key.ToString() ?? "", pair.Value?.ToString() ?? "");
            }
        }
    }
}
