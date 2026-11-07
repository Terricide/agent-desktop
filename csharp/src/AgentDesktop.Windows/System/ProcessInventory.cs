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

        var reported = Interop.GetProcessTimes(process, out var createLow, out var createHigh, out _, out _);
        Interop.CloseHandle(process);
        if (reported == 0)
        {
            return null;
        }

        var ticks = ((long)createHigh << 32) | createLow;
        if (ticks == 0)
        {
            return null;
        }

        return $"{TokenPrefix}:{ticks / TicksPerSecond}:{ticks % TicksPerSecond}";
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
