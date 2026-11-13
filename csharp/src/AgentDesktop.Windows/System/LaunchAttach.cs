using global::System;
using global::System.Collections.Generic;
using global::System.Linq;
using global::System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using static AgentDesktop.Windows.AppOps;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Chooses between starting a new process and attaching to a running instance of the same
/// executable, and returns the executable path the launch command should use.
/// </summary>
internal static class LaunchAttach
{
    /// <summary>
    /// Resolves the executable for a launch request. When attach is disabled, any process whose
    /// image name matches the request is a refusal. When attach is enabled, only a process whose
    /// full image path is the same file as the resolved executable can be attached to, and it
    /// must carry an exact process instance token.
    /// </summary>
    internal static (string Executable, LaunchResult? Result) Prepare(string id, LaunchOptions options, Deadline deadline)
    {
        var matches = MatchingProcesses(LaunchPath.ImageFileName(id));
        if (!options.AttachIfRunning)
        {
            if (matches.Count > 0)
            {
                throw LaunchSupport.AlreadyRunningError(matches[0].Pid, matches);
            }

            return (LaunchPath.ResolveExecutable(id), null);
        }

        var executable = LaunchPath.ResolveExecutable(id);
        var exactMatches = new List<(ProcessRow Row, string Token)>();
        var pins = new List<ProcessPin>();
        try
        {
            foreach (var row in matches)
            {
                var process = ProcessPin.Open(row.Pid);
                if (process is null)
                {
                    continue;
                }

                pins.Add(process);
                var imagePath = process.ImagePath();
                if (imagePath is null || !SameExecutablePath(executable, imagePath))
                {
                    continue;
                }

                var token = ProcessTimes.TokenForPid(row.Pid.Value)
                    ?? throw new AdapterError(
                        ErrorCode.AppUnresponsive,
                        "Running process has no exact process instance token");
                exactMatches.Add((row, token));
            }

            if (exactMatches.Count > 1)
            {
                throw LaunchSupport.AmbiguousApps(exactMatches.Select(match => match.Row).ToList());
            }

            if (exactMatches.Count == 1)
            {
                var (row, token) = exactMatches[0];
                var window = LaunchSupport.ObserveWindow(row.Pid, token, options, deadline);
                return (executable, LaunchSupport.LaunchResult(id, row.Pid, token, window));
            }

            return (executable, null);
        }
        finally
        {
            foreach (var pin in pins)
            {
                pin.Dispose();
            }
        }
    }

    /// <summary>
    /// Every running process whose image name matches the requested image, compared with ASCII
    /// case folding.
    /// </summary>
    internal static IReadOnlyList<ProcessRow> MatchingProcesses(string image)
    {
        return CaptureProcessRows()
            .Where(row => EqualsAsciiIgnoreCase(row.Name, image))
            .ToList();
    }

    /// <summary>
    /// True when both paths resolve to the same file. Canonicalization resolves junctions,
    /// symbolic links and short names, so two spellings of one file compare equal. A path that
    /// cannot be opened is not comparable.
    /// </summary>
    internal static bool SameExecutablePath(string expected, string actual)
    {
        var left = CanonicalPath(expected);
        if (left is null)
        {
            return false;
        }

        var right = CanonicalPath(actual);
        return right is not null && string.Equals(left, right, StringComparison.Ordinal);
    }

    private static bool EqualsAsciiIgnoreCase(string left, string right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (ToAsciiLower(left[index]) != ToAsciiLower(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static char ToAsciiLower(char value) =>
        value is >= 'A' and <= 'Z' ? (char)(value + 32) : value;

    private static string? CanonicalPath(string path)
    {
        var handle = NativeMethods.CreateFileW(
            path,
            NativeMethods.FileReadAttributes,
            NativeMethods.FileShareAll,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            NativeMethods.FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle == NativeMethods.InvalidHandleValue)
        {
            return null;
        }

        try
        {
            var buffer = new char[NativeMethods.PathBufferLength];
            var length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length)
            {
                return null;
            }

            return new string(buffer, 0, (int)length);
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>
    /// A process opened for one attach decision. The handle is released when the decision ends,
    /// so a long-running adapter does not accumulate process handles.
    /// </summary>
    private sealed class ProcessPin : IDisposable
    {
        private IntPtr _handle;

        private ProcessPin(IntPtr handle)
        {
            _handle = handle;
        }

        /// <summary>
        /// Opens a process for querying only. A process that has already exited, or one owned by
        /// another user, yields no pin.
        /// </summary>
        internal static ProcessPin? Open(ProcessId pid)
        {
            var handle = NativeMethods.OpenProcess(
                NativeMethods.ProcessQueryLimitedInformation,
                false,
                pid.Value);
            return handle == IntPtr.Zero ? null : new ProcessPin(handle);
        }

        /// <summary>
        /// The full image path of the pinned process, or null when the platform cannot read it.
        /// </summary>
        internal string? ImagePath()
        {
            var buffer = new char[NativeMethods.PathBufferLength];
            var size = (uint)buffer.Length;
            var ok = NativeMethods.QueryFullProcessImageNameW(_handle, 0, buffer, ref size);
            if (!ok || size == 0)
            {
                return null;
            }

            return new string(buffer, 0, (int)size);
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        internal const uint ProcessQueryLimitedInformation = 0x1000;
        internal const uint FileReadAttributes = 0x80;
        internal const uint FileShareAll = 0x7;
        internal const uint OpenExisting = 3;
        internal const uint FileFlagBackupSemantics = 0x02000000;
        internal const int PathBufferLength = 32_768;
        internal static readonly IntPtr InvalidHandleValue = new(-1);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] exeName, ref uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint GetFinalPathNameByHandleW(IntPtr file, [Out] char[] filePath, uint filePathLength, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
