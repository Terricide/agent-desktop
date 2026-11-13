namespace AgentDesktop.Core.Tests.Response;

using AgentDesktop.Core.Response;
using Xunit;

public class AppErrorTests
{
    [Fact]
    public void AdapterErrorMapsToStructuredErrorExit()
    {
        var appError = AppError.Stale("e1");

        Assert.Equal(ExitCodes.Error, Envelope.ExitCode(Envelope.Err("click", appError.Adapter!)));
    }

    [Fact]
    public void PermissionDeniedKeepsItsCode()
    {
        var appError = new AppError(
            AdapterError.FromCode(ErrorCode.PermDenied, "Accessibility permission is not granted"));

        Assert.Equal("PERM_DENIED", appError.Code);
    }
}
