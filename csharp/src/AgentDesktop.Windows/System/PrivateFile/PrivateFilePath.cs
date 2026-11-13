using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows;

/// <summary>Per-component path rules for private-file writes. Every directory from the root down to the
/// operation's parent is visited in order, parent traversal is refused, and a component that carries
/// `FILE_ATTRIBUTE_REPARSE_POINT` (a junction an unprivileged user can plant without
/// `SeCreateSymbolicLinkPrivilege`) or is not a directory is refused. A junction planted on a private path
/// redirects where the product writes, and no ACL on the intended destination prevents a write that never
/// reaches it. Reads guard only the leaf.</summary>
internal static class PrivateFilePath
{
    internal const uint FileAttributeDirectory = 0x10;
    internal const uint FileAttributeReparsePoint = 0x400;

    private static readonly char[] Separators = ['\\', '/'];

    internal delegate uint ComponentAttributes(string path);

    /// <summary>The directories to pin, root first, each one the accumulated prefix of the path. A leading
    /// current-directory component is skipped rather than rejected; a parent-traversal component is refused
    /// before any directory is touched.</summary>
    internal static IReadOnlyList<string> PinnedPrefixes(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        var prefixes = new List<string>();
        var current = root;
        if (root.Length > 0)
        {
            prefixes.Add(current);
        }

        foreach (var segment in path[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw AdapterError.InvalidArgs("private file parent must not contain parent traversal");
            }

            current = Path.Combine(current, segment);
            prefixes.Add(current);
        }

        return prefixes;
    }

    /// <summary>Walks every directory from the root down to the operation's parent and gates each one before
    /// the write resolves through it, so a junction planted mid-path is refused at the junction and no
    /// directory is created at its target.</summary>
    internal static void RequireVerifiedDirectoryChain(string parent, ComponentAttributes readAttributes)
    {
        foreach (var prefix in PinnedPrefixes(parent))
        {
            RequireVerifiedDirectory(readAttributes(prefix));
        }
    }

    /// <summary>Refuses a directory component that is a reparse point, then one that is not a directory.</summary>
    internal static void RequireVerifiedDirectory(uint attributes)
    {
        RequireNotReparsePoint(attributes, "private file path component is a reparse point");
        if ((attributes & FileAttributeDirectory) == 0)
        {
            throw new AdapterError(ErrorCode.PermDenied, "private file path component must be a directory");
        }
    }

    /// <summary>Refuses a leaf that is a reparse point, then one that is a directory, so only a regular file
    /// passes a private open.</summary>
    internal static void RequireRegularLeaf(uint attributes, string what)
    {
        RequireNotReparsePoint(attributes, $"{what} is a reparse point");
        if ((attributes & FileAttributeDirectory) != 0)
        {
            throw new AdapterError(ErrorCode.PermDenied, $"{what} is not a regular file");
        }
    }

    private static void RequireNotReparsePoint(uint attributes, string message)
    {
        if ((attributes & FileAttributeReparsePoint) != 0)
        {
            throw new AdapterError(ErrorCode.PermDenied, message);
        }
    }
}
