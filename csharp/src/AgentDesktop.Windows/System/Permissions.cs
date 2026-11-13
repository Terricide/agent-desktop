using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Capture;
using AgentDesktop.Windows.System.Runtime;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Reads the permission state this adapter can observe. Accessibility is the UI Automation COM runtime
/// reachability probe, screen recording is the capture backend availability, and automation needs no
/// permission on Windows. On hosts other than Windows the UI Automation probe reports E_NOTIMPL and the
/// capture probe reports no availability, so both read as unknown.
/// </summary>
internal static class Permissions
{
    private const string AccessibilitySuggestion =
        "Run agent-desktop in an interactive desktop session as a user allowed to use the UI Automation COM runtime; restricted tokens and AppContainer processes are denied UIA access.";

    private const int ENotImplemented = unchecked((int)0x80004001);
    private const uint CoinitMultithreaded = 0;
    private const int ClsctxInprocServer = 1;
    private const int SmCMonitors = 80;

    private static readonly global::System.Guid ClsidCuiAutomation = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private static readonly global::System.Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");

    /// <summary>Reads the permission report without requesting anything from the operating system.</summary>
    internal static PermissionReport Report(Deadline deadline)
    {
        deadline.EnsureRemaining();
        return ReportFromProbedUia(deadline, ProbeUiaAccess());
    }

    /// <summary>
    /// Probes UI Automation access and fails with <c>PERM_DENIED</c> when the runtime denies it. Otherwise
    /// returns the same report <see cref="Report"/> would.
    /// </summary>
    internal static PermissionReport RequestReport(Deadline deadline) =>
        RequestReportWith(deadline, ProbeUiaAccess, ReportFromProbedUia);

    private static PermissionReport RequestReportWith(
        Deadline deadline,
        global::System.Func<int> probe,
        global::System.Func<Deadline, int, PermissionReport> report)
    {
        deadline.EnsureRemaining();
        var hresult = probe();
        deadline.EnsureRemaining();
        if (MapUiaAccess(hresult) == PermissionState.Denied)
        {
            throw UiaAccessDeniedError(hresult);
        }

        return report(deadline, hresult);
    }

    private static PermissionReport ReportFromProbedUia(Deadline deadline, int uiaHresult)
    {
        var report = new PermissionReport
        {
            Accessibility = MapUiaAccess(uiaHresult),
            ScreenRecording = ScreenRecordingReportState(),
            Automation = AutomationReportState(),
        };
        deadline.EnsureRemaining();
        return report;
    }

    /// <summary>
    /// Maps the UI Automation probe HRESULT to a permission state. <c>S_OK</c> is granted and
    /// <c>E_ACCESSDENIED</c> is denied; every other HRESULT is unknown, because it says nothing about the user's
    /// grant.
    /// </summary>
    internal static PermissionState MapUiaAccess(int hresult) => hresult switch
    {
        HResult.SOk => PermissionState.Granted,
        HResult.EAccessDenied => PermissionState.Denied,
        _ => PermissionState.Unknown,
    };

    /// <summary>A capture backend that can serve the session needs no permission; an absent probe is unknown.</summary>
    internal static PermissionState MapCaptureAvailability(bool? availability) =>
        availability == true ? PermissionState.NotRequired : PermissionState.Unknown;

    /// <summary>
    /// The <c>PERM_DENIED</c> error for a denied UI Automation probe. The platform detail carries the HRESULT,
    /// and the suggestion names the session and token the caller must run under.
    /// </summary>
    internal static AdapterError UiaAccessDeniedError(int hresult) =>
        new AdapterError(ErrorCode.PermDenied, "UI Automation access is denied for this process")
            .WithSuggestion(AccessibilitySuggestion)
            .WithPlatformDetail(HResult.ComHresultDetail(hresult));

    private static PermissionState ScreenRecordingReportState() =>
        MapCaptureAvailability(ProbeCaptureAvailability());

    private static PermissionState AutomationReportState() => PermissionState.NotRequired;

    private static int ProbeUiaAccess()
    {
        if (!global::System.OperatingSystem.IsWindows())
        {
            return ENotImplemented;
        }

        var initStatus = Native.CoInitializeEx(nint.Zero, CoinitMultithreaded);
        if (!ComRuntime.TryClassifyCoInitializeHresult(initStatus, out var apartment))
        {
            return initStatus;
        }

        var createStatus = Calls.CoCreateInstance(in ClsidCuiAutomation, nint.Zero, ClsctxInprocServer, in IidIUnknown, out var instance);
        Calls.Release(instance);
        if (ComRuntime.PermitsCoUninitialize(apartment))
        {
            Native.CoUninitialize();
        }

        return createStatus;
    }

    private static bool? ProbeCaptureAvailability()
    {
        if (!global::System.OperatingSystem.IsWindows())
        {
            return null;
        }

        if (CaptureBackend.ModernIsSupported())
        {
            return true;
        }

        return LegacyDisplayCapturePossible();
    }

    private static bool LegacyDisplayCapturePossible() => Native.GetSystemMetrics(SmCMonitors) > 0;

    private static class Native
    {
        [global::System.Runtime.InteropServices.DllImport("ole32.dll")]
        internal static extern int CoInitializeEx(nint reserved, uint coInit);

        [global::System.Runtime.InteropServices.DllImport("ole32.dll")]
        internal static extern void CoUninitialize();

        [global::System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int index);
    }
}
