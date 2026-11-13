using global::System.Collections.Generic;
using global::System.Linq;
using global::System.Runtime.InteropServices;
using global::System.Runtime.Versioning;
using global::System.Text;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Finds every endpoint a session is answering on. A stop names no agent: stopping a session
/// stops all of it, however many agents drew inside it. The endpoints are found by listing the
/// named pipes the OS is serving and keeping those whose names begin with the session segment,
/// because the pipe name places the session segment first so that the prefix makes the listing
/// answerable.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Discover
{
    private const string PipeDirectory = @"\\.\pipe\*";
    private const string PipePrefix = @"\\.\pipe\";
    private const int FindDataSize = 592;
    private const int FileNameOffset = 44;
    private const int FileNameCapacity = 260;

    /// <summary>
    /// The names this session is answering on right now, including the one it answers on when no
    /// agent was named. An empty result is not evidence that a session has no renderer: a failed
    /// enumeration answers the same way. The caller uses this to widen a teardown, never to
    /// conclude one is unnecessary, so a failed listing costs a broadcast its extra recipients
    /// rather than reporting a session already stopped.
    /// </summary>
    internal static IReadOnlyList<string> SessionEndpoints(string root, string sessionId)
    {
        var prefix = PipeName.BuildSessionPrefix(root, sessionId);
        var found = new List<string> { PipeName.Build(root, sessionId, null) };
        foreach (var name in ListPipes())
        {
            if (name.StartsWith(prefix, global::System.StringComparison.Ordinal))
            {
                found.Add(PipePrefix + name);
            }
        }

        return found
            .Distinct(global::System.StringComparer.Ordinal)
            .OrderBy(endpoint => endpoint, global::System.StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> ListPipes()
    {
        var names = new List<string>();
        var data = Marshal.AllocHGlobal(FindDataSize);
        try
        {
            var handle = FindFirstFileW(PipeDirectory, data);
            if (handle == new global::System.IntPtr(-1))
            {
                return names;
            }

            try
            {
                do
                {
                    names.Add(FileNameOf(data));
                }
                while (FindNextFileW(handle, data) != 0);
            }
            finally
            {
                FindClose(handle);
            }

            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    /// <summary>
    /// The entry's name, cut at its terminator. The file-name field is a fixed-width buffer whose
    /// tail is whatever the previous entry left there, so reading past the terminator would append
    /// the remains of a longer neighbour's name.
    /// </summary>
    private static string FileNameOf(global::System.IntPtr data)
    {
        var name = new StringBuilder();
        for (var index = 0; index < FileNameCapacity; index++)
        {
            var character = (char)Marshal.ReadInt16(data, FileNameOffset + (index * 2));
            if (character == '\0')
            {
                break;
            }

            name.Append(character);
        }

        return name.ToString();
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileW", CharSet = CharSet.Unicode)]
    private static extern global::System.IntPtr FindFirstFileW(string fileName, global::System.IntPtr findFileData);

    [DllImport("kernel32.dll", EntryPoint = "FindNextFileW", CharSet = CharSet.Unicode)]
    private static extern int FindNextFileW(global::System.IntPtr findFile, global::System.IntPtr findFileData);

    [DllImport("kernel32.dll", EntryPoint = "FindClose")]
    private static extern int FindClose(global::System.IntPtr findFile);
}
