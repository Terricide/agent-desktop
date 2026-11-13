using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;
using AgentDesktop.Windows.System.Runtime;
using Xunit;

namespace AgentDesktop.Windows.Tests.System;

/// <summary>
/// The permission report is a read: it observes the state this process can reach and asks the operating
/// system for nothing. So the probes it runs answer with a state, and never with a refusal. A probe that
/// refuses turns an unavailable backend into an error, which fails a command that only reads, and reports
/// a permission as a failure when nothing was denied.
/// </summary>
public class PermissionsTests
{
    [Fact]
    public void TheReportReadsStateWithoutAskingTheOperatingSystemForAnything()
    {
        var report = Permissions.Report(Deadline.Standard());

        Assert.NotNull(report);
        Assert.Equal(PermissionState.NotRequired, report.Automation);
    }

    [Fact]
    public void TheReportOnADesktopSessionReadsUiAutomationAsGranted()
    {
        var report = Permissions.Report(Deadline.Standard());

        Assert.Equal(PermissionState.Granted, report.Accessibility);
    }

    [Fact]
    public void AnUnavailableCaptureBackendIsNotADeniedPermission()
    {
        Assert.Equal(PermissionState.NotRequired, Permissions.MapCaptureAvailability(true));
        Assert.Equal(PermissionState.Unknown, Permissions.MapCaptureAvailability(null));
    }

    [Fact]
    public void OnlyTheAccessDeniedHresultReadsAsDenied()
    {
        Assert.Equal(PermissionState.Granted, Permissions.MapUiaAccess(HResult.SOk));
        Assert.Equal(PermissionState.Denied, Permissions.MapUiaAccess(HResult.EAccessDenied));
        Assert.Equal(PermissionState.Unknown, Permissions.MapUiaAccess(unchecked((int)0x80004001)));
        Assert.Equal(PermissionState.Unknown, Permissions.MapUiaAccess(HResult.ENoInterface));
        Assert.Equal(PermissionState.Unknown, Permissions.MapUiaAccess(HResult.CoENotInitialized));
        Assert.Equal(PermissionState.Unknown, Permissions.MapUiaAccess(HResult.RpcEDisconnected));
    }

    [Fact]
    public void ADeniedProbeNamesTheSessionTheCallerMustRunUnder()
    {
        var error = Permissions.UiaAccessDeniedError(HResult.EAccessDenied);

        Assert.Equal(ErrorCode.PermDenied, error.Code);
        Assert.Contains("interactive desktop session", error.Suggestion);
    }
}
