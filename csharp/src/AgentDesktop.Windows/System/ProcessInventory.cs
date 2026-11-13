using System.Runtime.InteropServices;

namespace AgentDesktop.Windows;

/// <summary>Reads the process-generation token from the kernel: the process creation time,
/// which survives handle and pid recycling. The token is the identity the shared contract
/// compares, so a process the kernel will not name gets no token, and a token is never
/// invented from a pid alone.</summary>
internal static class ProcessTimes
{
    private const string TokenPrefix = "windows-proc-v1";
    private const long TicksPerSecond = 10_000_000;
    private const int QueryLimitedInformation = 0x1000;

    /// <summary>The generation token for a pid, or nothing when the kernel will not open the
    /// process or will not report its creation time.</summary>
    internal static string? TokenForPid(uint pid)
    {
        var process = Interop.OpenProcess(QueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        var reported = Interop.GetProcessTimes(process, out var creation, out _, out _, out _);
        Interop.CloseHandle(process);
        if (reported == 0)
        {
            return null;
        }

        var ticks = ((long)creation.High << 32) | creation.Low;
        if (ticks == 0)
        {
            return null;
        }

        return $"{TokenPrefix}:{ticks / TicksPerSecond}:{ticks % TicksPerSecond}";
    }

    /// <summary>Verifies a stored token against the process's current generation. A recycled pid
    /// whose process is a different generation fails closed; a pid whose process is gone
    /// altogether reads no token and also fails. This is the check a recycled window handle on a
    /// different process generation trips.</summary>
    internal static bool MatchesInstance(uint pid, string token) =>
        ParseToken(token) is { } expected && expected == TokenForPid(pid);

    /// <summary>Parses a token back into the generation it names, or nothing for an unrecognised
    /// shape, which must not match anything.</summary>
    internal static string? ParseToken(string token)
    {
        var parts = token.Split(':');
        if (parts.Length != 3 || parts[0] != TokenPrefix)
        {
            return null;
        }

        if (!long.TryParse(parts[1], out var seconds) || !long.TryParse(parts[2], out var subtick))
        {
            return null;
        }

        return $"{TokenPrefix}:{seconds}:{subtick}";
    }

    private const int Synchronize = 0x100000;

    private const uint WaitObjectZero = 0;

    private const uint InvalidParameter = 87;

    /// <summary>Whether the process at `pid` is running. A handle that cannot be opened is not
    /// evidence the process is gone, so it reports running; only a signaled process object is
    /// evidence it exited.</summary>
    internal static bool ProcessIsRunning(uint pid)
    {
        var process = Interop.OpenProcess(QueryLimitedInformation | Synchronize, false, pid);
        if (process == IntPtr.Zero)
        {
            return true;
        }

        var wait = Interop.WaitForSingleObject(process, 0);
        Interop.CloseHandle(process);
        return wait != WaitObjectZero;
    }

    /// <summary>Blocks on the process object itself for at most `window` and reports whether the
    /// process exited inside it. `ProcessIsRunning` is a zero-timeout poll, so a process that is
    /// tearing down but not yet signaled reads as running. This waits on the handle, an event
    /// rather than a sleep, so the exit is observed the instant it happens. A pid that no longer
    /// names a process has exited; any other failure to open the handle cannot confirm an exit and
    /// reports false without waiting.</summary>
    internal static bool WaitForExit(uint pid, TimeSpan window)
    {
        var process = Interop.OpenProcess(QueryLimitedInformation | Synchronize, false, pid);
        if (process == IntPtr.Zero)
        {
            return Interop.GetLastError() == InvalidParameter;
        }

        var millis = (uint)Math.Min(window.TotalMilliseconds, uint.MaxValue);
        var wait = Interop.WaitForSingleObject(process, millis);
        Interop.CloseHandle(process);
        return wait == WaitObjectZero;
    }
}

/// <summary>The process table captured once for a pass: pid to image name, read from one
/// ToolHelp snapshot so a pass of many windows costs one snapshot, not one read per window.
/// A pid the snapshot does not name is reported as unnamed, never as a name guessed from
/// the window.</summary>
internal sealed class ProcessSnapshot
{
    private const uint SnapshotOfProcesses = 0x00000002;
    private const uint InvalidHandle = 0xFFFFFFFF;

    private readonly Dictionary<uint, string> _imageNames;

    private ProcessSnapshot(Dictionary<uint, string> imageNames) => _imageNames = imageNames;

    /// <summary>Captures the process table. A snapshot the platform refuses leaves the table
    /// empty, so every pid is reported unnamed and the pass keeps the windows it can name.</summary>
    internal static ProcessSnapshot Capture()
    {
        var imageNames = new Dictionary<uint, string>();
        var snapshot = Interop.CreateToolhelp32Snapshot(SnapshotOfProcesses, 0);
        if (snapshot == IntPtr.Zero || snapshot.ToInt64() == unchecked((long)InvalidHandle))
        {
            return new ProcessSnapshot(imageNames);
        }

        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (Interop.Process32FirstW(snapshot, ref entry) != 0)
        {
            do
            {
                imageNames[entry.ProcessId] = entry.Name;
            }
            while (Interop.Process32NextW(snapshot, ref entry) != 0);
        }

        Interop.CloseHandle(snapshot);
        return new ProcessSnapshot(imageNames);
    }

    /// <summary>The image name a pid owns, or nothing when the table does not name it.</summary>
    internal string? ImageNameFor(uint pid) => _imageNames.TryGetValue(pid, out var name) ? name : null;
}
