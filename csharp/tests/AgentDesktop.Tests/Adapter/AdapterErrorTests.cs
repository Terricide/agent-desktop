namespace AgentDesktop.Core.Tests.Adapter;

using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Response;
using Xunit;

public class AdapterErrorTests
{
    [Fact]
    public void StaleRecoversByRefreshingTheSnapshot()
    {
        var error = AdapterError.Stale("e1");

        Assert.Equal(ErrorCode.StaleRef, error.Code);
        Assert.Equal(RecoveryStrategy.RefreshSnapshotThenRetryOriginal, error.Recovery!.Strategy);
    }

    [Fact]
    public void NotSupportedCarriesTheDocumentedCode()
    {
        var error = AdapterError.NotSupported("capture_region");

        Assert.Equal(ErrorCode.PlatformNotSupported, error.Code);
        Assert.Equal("PLATFORM_NOT_SUPPORTED", error.Code.AsString());
    }

    [Fact]
    public void FromCodeCarriesADispositionAndNoRecoveryWhenRetryIsNotSafe()
    {
        var error = AdapterError.FromCode(ErrorCode.PermDenied, "Accessibility permission is not granted");

        Assert.NotNull(error.Disposition);
        Assert.Equal(Retry.Unknown, error.Disposition.Retry);
        Assert.Null(error.Recovery);
    }

    [Fact]
    public void RecoveryIsOfferedOnlyWhenRetryIsSafe()
    {
        var dispatched = AdapterError
            .FromCode(ErrorCode.ActionFailed, "The write failed after dispatch")
            .WithDisposition(DeliverySemantics.DeliveredUnverified());
        Assert.Null(dispatched.Recovery);

        var unsent = AdapterError.Stale("e1");
        Assert.Equal(Retry.Safe, unsent.Disposition.Retry);
        Assert.Equal(RecoveryStrategy.RefreshSnapshotThenRetryOriginal, unsent.Recovery!.Strategy);
    }
}
