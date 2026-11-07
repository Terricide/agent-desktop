using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using Xunit;

namespace AgentDesktop.Tests.Locator;

public class StrictResolverTests
{
    private static RefEntry Entry(string role, string nativeId, int x) =>
        new()
        {
            Identity = new RefEntryIdentity { Role = role, NativeId = nativeId, WindowKey = "w1" },
            Geometry = new RefGeometry { Bounds = new Rect(x, 0, 100, 20) },
        };

    [Fact]
    public void ZeroLiveMatchesIsAStaleRef()
    {
        var result = StrictResolver.Resolve(new List<RefEntry>(), Entry("Button", "ok", 0));

        Assert.Equal(ResolutionOutcome.Stale, result.Outcome);
        Assert.Contains("\"code\":\"STALE_REF\"",
            Envelope.Err("click", AdapterError.Stale("@s1:e1")).ToJsonString());
    }

    [Fact]
    public void TwoOrMoreMatchesIsAnAmbiguousTarget()
    {
        var observed = new List<RefEntry> { Entry("Button", "ok", 0), Entry("Button", "ok", 300) };
        var result = StrictResolver.Resolve(observed, observed[0]);
        Assert.Equal(ResolutionOutcome.Ambiguous, result.Outcome);
        Assert.Equal(2, result.MatchCount);
    }

    [Fact]
    public void ARefThatWasNotUniqueAtCaptureIsRefusedBeforeTheApplicationIsRead()
    {
        var target = Entry("Button", "ok", 0) with { };
        var refused = new RefEntry
        {
            Identity = new RefEntryIdentity { Role = "Button", NativeId = "ok", WindowKey = "w1", UniquenessReason = "role+name matched 3 elements" },
        };
        var result = StrictResolver.Resolve(new List<RefEntry> { target }, refused);
        Assert.Equal(ResolutionOutcome.RefusedAtCapture, result.Outcome);
        Assert.Null(result.Match);
    }

    [Fact]
    public void BoundsAreNotIdentity()
    {
        var captured = Entry("Button", "ok", 10);
        var live = Entry("Button", "ok", 400);
        var result = StrictResolver.Resolve(new List<RefEntry> { live }, captured);
        Assert.Equal(ResolutionOutcome.Unique, result.Outcome);
        Assert.Same(live, result.Match);
    }
}
