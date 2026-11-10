using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Verifies the window identity evidence: the listed shape that yields no evidence, and the
/// error shape that names the handle and never the title. The two directions of the stored/strict
/// split need a window this process owns and a window another process owns; the Windows tree fixture
/// harness is not ported, so no fixture handle exists to verify them against.</summary>
public sealed class WindowIdentityEvidenceTests
{
    private static WindowInfo FakeWindow(ProcessId pid, string instance, string title) =>
        new WindowInfo
        {
            Id = "w-1",
            Title = title,
            App = "fixture",
            Pid = pid,
            ProcessInstance = instance,
        };

    [Fact]
    public void AShapeWithNoProcessInstanceFailsClosed()
    {
        var win = FakeWindow(ProcessId.From(1), "x", "T") with { ProcessInstance = null };

        Assert.Null(WindowIdentityEvidence.FromInfo(IntPtr.Zero, win));
    }

    /// <summary>A window whose title carries a marker string must not leak it into the
    /// <c>WINDOW_NOT_FOUND</c> error when identity verification fails: the error shape is id-only by
    /// construction, handle and never title, and this pins it so it stays that way.</summary>
    [Fact]
    public void AMarkerTitledWindowThatFailsIdentityLeaksNoMarker()
    {
        const string marker = "unredacted-secret-marker-x7f2";
        var fake = FakeWindow(ProcessId.From(9_999_999), "windows-proc-v1:0:0", marker);

        if (WindowIdentityEvidence.FromInfo(IntPtr.Zero, fake) is not { } evidence)
        {
            Assert.Fail("a process instance yields evidence");
            return;
        }

        var error = Assert.Throws<AdapterError>(() => evidence.VerifyStored());

        Assert.Equal(ErrorCode.WindowNotFound, error.Code);
        Assert.DoesNotContain(marker, error.Message);
        Assert.DoesNotContain(marker, error.Details ?? "");
        Assert.DoesNotContain(marker, error.PlatformDetail ?? "");
    }
}
