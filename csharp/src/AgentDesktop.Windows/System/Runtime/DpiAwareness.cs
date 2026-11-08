using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows;

/// <summary>Establishes per-monitor-v2 DPI awareness for this process, judged on the call's
/// return alone: awareness is never read back, because GetProcessDpiAwareness has no V2
/// enumerant and reports V2 as V1. ERROR_ACCESS_DENIED means the process or its host already
/// fixed the awareness context, which is success, not failure. Without it a window rect is
/// read in the virtualized coordinates a DPI-unaware process is handed, and every bound the
/// envelope reports is scaled by the session DPI.</summary>
internal static class DpiAwareness
{
    private const int AlreadySetError = 5;

    private static readonly IntPtr PerMonitorV2 = new(-4);

    internal static void EnsurePerMonitorV2()
    {
        if (Interop.SetProcessDpiAwarenessContext(PerMonitorV2) != 0)
        {
            return;
        }

        var lastError = Marshal.GetLastWin32Error();
        if (lastError != AlreadySetError)
        {
            throw new AdapterError(
                ErrorCode.Internal,
                "Per-monitor-v2 DPI awareness could not be established for this process")
                .WithPlatformDetail($"SetProcessDpiAwarenessContext Win32 error {lastError}");
        }
    }
}
