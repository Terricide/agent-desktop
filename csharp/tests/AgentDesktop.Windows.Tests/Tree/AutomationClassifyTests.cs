using AgentDesktop.Core.Response;
using AgentDesktop.Windows.System.Runtime;
using AgentDesktop.Windows.Tree;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>
/// Pins the UI Automation failure classification: the sentinel and HRESULT dispositions, the complete and
/// retryable stamps core reads, the window-root rewrite, and the absence predicate that separates "no match"
/// from a fault. Every assertion is on the consequence a caller observes, never on a restated table.
/// </summary>
public sealed class AutomationClassifyTests
{
    /// <summary>One sentinel's classification as a person decided it, written as literals.</summary>
    private sealed record ExpectedSentinel(
        int Sentinel,
        ErrorCode Code,
        AutomationClassify.ReadDisposition Disposition,
        bool Complete,
        bool Retryable);

    private static IReadOnlyList<ExpectedSentinel> ExpectedSentinels() =>
    [
        new(AutomationClassify.ErrNotFound, ErrorCode.ElementNotFound, AutomationClassify.ReadDisposition.Unavailable, false, true),
        new(AutomationClassify.ErrNullPtr, ErrorCode.ElementNotFound, AutomationClassify.ReadDisposition.Terminal, true, false),
        new(AutomationClassify.ErrTimeout, ErrorCode.Timeout, AutomationClassify.ReadDisposition.Retryable, false, true),
        new(AutomationClassify.ErrInactive, ErrorCode.AppUnresponsive, AutomationClassify.ReadDisposition.Retryable, false, true),
        new(AutomationClassify.ErrInvalidObject, ErrorCode.StaleRef, AutomationClassify.ReadDisposition.Unavailable, false, true),
        new(AutomationClassify.ErrInvalidArg, ErrorCode.InvalidArgs, AutomationClassify.ReadDisposition.SettledAbsence, true, false),
        new(AutomationClassify.ErrNone, ErrorCode.Internal, AutomationClassify.ReadDisposition.Terminal, true, false),
        new(AutomationClassify.ErrType, ErrorCode.Internal, AutomationClassify.ReadDisposition.Terminal, true, false),
        new(AutomationClassify.ErrFormat, ErrorCode.Internal, AutomationClassify.ReadDisposition.Terminal, true, false),
        new(AutomationClassify.ErrAlreadyRunning, ErrorCode.Internal, AutomationClassify.ReadDisposition.Terminal, true, false),
    ];

    [Fact]
    public void AVanishedElementsSentinelPairsStaleRefWithTheUnavailableDisposition()
    {
        var failure = AutomationClassify.Failure.Sentinel(AutomationClassify.ErrInvalidObject);

        Assert.Equal(AutomationClassify.ReadDisposition.Unavailable, AutomationClassify.DispositionOf(failure));
        Assert.Equal(ErrorCode.StaleRef, AutomationClassify.UiaFailureError(failure, "read a property").Code);
    }

    [Fact]
    public void EverySentinelCarriesTheClassificationAPersonDecidedForIt()
    {
        foreach (var expected in ExpectedSentinels())
        {
            var failure = AutomationClassify.Failure.Sentinel(expected.Sentinel);
            var error = AutomationClassify.UiaFailureError(failure, "read a property");

            Assert.True(expected.Code == error.Code, $"sentinel {expected.Sentinel} surfaced code");
            Assert.True(expected.Disposition == AutomationClassify.DispositionOf(failure),
                $"sentinel {expected.Sentinel} disposition");
            Assert.True((expected.Complete, expected.Retryable) == AutomationClassify.RetryDetails(expected.Disposition),
                $"sentinel {expected.Sentinel} complete/retryable pair");
            Assert.True(expected.Retryable == error.IsExplicitlyRetryable,
                $"sentinel {expected.Sentinel} surfaced retryability");
        }
    }

    [Fact]
    public void TheExpectationTableCoversEveryNamedSentinel()
    {
        var named = new[]
        {
            AutomationClassify.ErrNotFound,
            AutomationClassify.ErrNullPtr,
            AutomationClassify.ErrTimeout,
            AutomationClassify.ErrInactive,
            AutomationClassify.ErrInvalidObject,
            AutomationClassify.ErrInvalidArg,
            AutomationClassify.ErrNone,
            AutomationClassify.ErrType,
            AutomationClassify.ErrFormat,
            AutomationClassify.ErrAlreadyRunning,
        };
        var expectations = ExpectedSentinels();

        Assert.NotEmpty(expectations);
        foreach (var sentinel in named)
        {
            Assert.Contains(expectations, expected => expected.Sentinel == sentinel);
        }

        Assert.Equal(named.Length, expectations.Count);
    }

    [Fact]
    public void AMissingWindowRootSettlesRatherThanPermittingAPointlessRetry()
    {
        foreach (var failure in new[]
        {
            AutomationClassify.Failure.Hresult(UiaFailure.ElementNotAvailable),
            AutomationClassify.Failure.Sentinel(AutomationClassify.ErrNotFound),
        })
        {
            var error = AutomationClassify.RootResolutionError(failure);

            Assert.Equal(ErrorCode.WindowNotFound, error.Code);
            Assert.False(error.IsExplicitlyRetryable, "a window that is gone must not be marked retryable");
            Assert.False(error.PermitsRetryByDefault, "an unstamped rewrite reads as retry-permitting; the stamp must deny it");
        }
    }

    [Fact]
    public void ARootFailureThatIsNotAMissingWindowKeepsItsOwnRetryStamp()
    {
        var transport = AutomationClassify.RootResolutionError(AutomationClassify.Failure.Sentinel(AutomationClassify.ErrTimeout));
        var settled = AutomationClassify.RootResolutionError(AutomationClassify.Failure.Sentinel(AutomationClassify.ErrInvalidArg));

        Assert.True(transport.IsExplicitlyRetryable);
        Assert.False(settled.IsExplicitlyRetryable);
        Assert.False(settled.PermitsRetryByDefault);
    }

    [Fact]
    public void ExhaustionAndTheVanishedFamilyAreAbsenceAndAnythingElseIsAFault()
    {
        Assert.True(AutomationClassify.Failure.Sentinel(AutomationClassify.ErrNone).IsAbsence);
        Assert.True(AutomationClassify.Failure.Sentinel(AutomationClassify.ErrNotFound).IsAbsence);
        Assert.True(AutomationClassify.Failure.Hresult(UiaFailure.ElementNotAvailable).IsAbsence);

        Assert.False(AutomationClassify.Failure.Sentinel(AutomationClassify.ErrTimeout).IsAbsence);
        Assert.False(AutomationClassify.Failure.Hresult(HResult.UiaETimeout).IsAbsence);
        Assert.False(AutomationClassify.Failure.Sentinel(AutomationClassify.ErrInvalidObject).IsAbsence);
        Assert.False(AutomationClassify.Failure.Sentinel(AutomationClassify.ErrNullPtr).IsAbsence);
    }
}
