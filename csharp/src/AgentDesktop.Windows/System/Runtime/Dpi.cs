using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows;

/// <summary>Establishes per-monitor-v2 DPI awareness for this process, judged on the call's
/// return alone: awareness is never read back, because GetProcessDpiAwareness has no V2
/// enumerant and reports V2 as V1. ERROR_ACCESS_DENIED means the process or its host already
/// fixed the awareness context, which is success, not failure.</summary>
internal static class Dpi
{
    private const uint DpiContextAlreadySetError = 5;

    private static readonly IntPtr PerMonitorV2 = new(-4);

    /// <summary>Applies DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 to this process and returns
    /// which outcome held. Any other Win32 failure raises an Internal AdapterError that carries
    /// the Win32 error code in its platform detail.</summary>
    internal static DpiAwarenessOutcome EnsurePerMonitorV2()
    {
        var callSucceeded = Interop.SetProcessDpiAwarenessContext(PerMonitorV2) != 0;
        var lastError = callSucceeded ? 0u : (uint)Marshal.GetLastWin32Error();

        if (TryClassifyDpiAwarenessCall(callSucceeded, lastError, out var outcome, out var failure))
        {
            return outcome;
        }

        throw DpiAwarenessFailure(failure);
    }

    internal static bool TryClassifyDpiAwarenessCall(
        bool callSucceeded,
        uint lastError,
        out DpiAwarenessOutcome outcome,
        out uint failure)
    {
        if (callSucceeded)
        {
            outcome = DpiAwarenessOutcome.PerMonitorV2Applied;
            failure = 0;
            return true;
        }

        if (lastError == DpiContextAlreadySetError)
        {
            outcome = DpiAwarenessOutcome.AlreadySet;
            failure = 0;
            return true;
        }

        outcome = default;
        failure = lastError;
        return false;
    }

    private static AdapterError DpiAwarenessFailure(uint lastError)
    {
        return new AdapterError(
            ErrorCode.Internal,
            "Per-monitor-v2 DPI awareness could not be established for this process")
            .WithPlatformDetail($"SetProcessDpiAwarenessContext Win32 error {lastError}")
            .WithSuggestion(
                "Rerun from a process whose host has not locked an incompatible DPI awareness context");
    }
}

internal enum DpiAwarenessOutcome
{
    PerMonitorV2Applied,
    AlreadySet,
}
