using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// One <c>TH32CS_SNAPTHREAD</c> walk of every thread on the desktop, feeding each entry to a visitor until it
/// returns a value, the walk runs out of threads, or the deadline expires.
/// </summary>
/// <remarks>
/// <c>CreateToolhelp32Snapshot</c> reports failure as <c>INVALID_HANDLE_VALUE</c> (<c>-1</c>), never as a null
/// handle, so the guard compares against <c>-1</c>. A null-handle guard can never fire, and a failed call would
/// read as an empty enumeration rather than an error.
/// A caller that short-circuits on the first match pays no budget check at all. A caller that must see every
/// thread pays exactly one budget read per thread after the first.
/// The snapshot handle is closed on every exit path: running out of threads, an early match, a deadline expiring
/// mid-walk, and a visitor that throws. A poller that repeats this walk every 200ms leaks one handle per expired
/// capture if any one of those paths skips the close.
/// </remarks>
internal static class ThreadWalk
{
    private const uint SnapshotOfThreads = 0x00000004;
    private static readonly IntPtr InvalidHandleValue = new(-1);
    private const string OpenFailureMessage = "CreateToolhelp32Snapshot failed for classic menu-mode detection";

    internal static T? WalkGuiThreads<T>(Deadline deadline, Func<ThreadEntry, T?> onThread)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotOfThreads, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw AdapterErrorFromWin32(Marshal.GetLastPInvokeError(), OpenFailureMessage);
        }

        try
        {
            var entry = new ThreadEntry { Size = (uint)Marshal.SizeOf<ThreadEntry>() };
            var first = true;
            var ok = Thread32First(snapshot, ref entry) != 0;
            while (ok)
            {
                if (!first)
                {
                    deadline.EnsureRemaining();
                }

                first = false;
                var value = onThread(entry);
                if (value is not null)
                {
                    return value;
                }

                ok = Thread32Next(snapshot, ref entry) != 0;
            }

            return default;
        }
        finally
        {
            Interop.CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// The Win32 last error, read immediately after the failing call, rendered through the shared HRESULT table.
    /// </summary>
    private static AdapterError AdapterErrorFromWin32(int lastError, string message) =>
        HResult.AdapterErrorFromWin32((uint)lastError, message);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int Thread32First(IntPtr snapshot, ref ThreadEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int Thread32Next(IntPtr snapshot, ref ThreadEntry entry);
}
