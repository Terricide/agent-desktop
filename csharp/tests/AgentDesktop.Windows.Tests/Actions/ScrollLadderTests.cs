using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Actions;
using Xunit;

namespace AgentDesktop.Windows.Tests.Actions;

/// <summary>Geometry and ladder judgment for the ancestor scroll ladder. Every case is pure: no live UIA tree is needed.</summary>
public sealed class ScrollLadderTests
{
    private static Rect R(double x, double y, double width, double height) => new(x, y, width, height);

    [Fact]
    public void DirectionPriorityMatchesMacosVerticalBeforeHorizontal()
    {
        var viewport = R(100.0, 100.0, 200.0, 200.0);
        Assert.Equal<Direction?>(Direction.Up, ScrollLadder.DirectionForVisibility(R(150.0, 50.0, 40.0, 20.0), viewport));
        Assert.Equal<Direction?>(Direction.Down, ScrollLadder.DirectionForVisibility(R(150.0, 350.0, 40.0, 20.0), viewport));
        Assert.Equal<Direction?>(Direction.Left, ScrollLadder.DirectionForVisibility(R(40.0, 150.0, 40.0, 20.0), viewport));
        Assert.Equal<Direction?>(Direction.Right, ScrollLadder.DirectionForVisibility(R(350.0, 150.0, 40.0, 20.0), viewport));
        Assert.Null(ScrollLadder.DirectionForVisibility(R(120.0, 120.0, 40.0, 20.0), viewport));
    }

    [Fact]
    public void VerticalEdgeWinsWhenBothAxesAreOut()
    {
        var viewport = R(0.0, 0.0, 100.0, 100.0);
        Assert.Equal<Direction?>(Direction.Down, ScrollLadder.DirectionForVisibility(R(200.0, 200.0, 10.0, 10.0), viewport));
        Assert.Equal<Direction?>(Direction.Up, ScrollLadder.DirectionForVisibility(R(-20.0, -20.0, 10.0, 10.0), viewport));
    }

    [Fact]
    public void VisibleAtRungZeroIsSatisfiedNoDelivery()
    {
        var scrolls = 0;
        var outcome = ScrollLadder.LadderJudgedFor(
            Deadline.After(5_000),
            () => null,
            _ => scrolls++);
        Assert.Equal(DeliveryOutcome.SatisfiedNoDelivery, outcome);
        Assert.Equal(0, scrolls);
    }

    [Fact]
    public void VisibleAfterNRungsIsDeliveredVerified()
    {
        var scrolls = 0;
        var outcome = ScrollLadder.LadderJudgedFor(
            Deadline.After(5_000),
            () => scrolls < 3 ? Direction.Down : null,
            _ => scrolls++);
        Assert.Equal(DeliveryOutcome.DeliveredVerified, outcome);
        Assert.Equal(3, scrolls);
    }

    [Fact]
    public void UnclippedBelowFoldDoesNotVerifyWithoutIntersection()
    {
        var viewport = R(0.0, 0.0, 100.0, 100.0);
        var belowFold = new VisibilitySample(R(10.0, 150.0, 80.0, 200.0), false, viewport);
        Assert.False(ScrollLadder.VisibilityVerified(belowFold));
        var seen = false;
        var calls = 0;
        var error = Assert.Throws<AdapterError>(() =>
        {
            ScrollLadder.LadderJudgedFor(
                Deadline.After(5_000),
                () =>
                {
                    if (ScrollLadder.VisibilityVerified(belowFold))
                    {
                        return null;
                    }

                    seen = true;
                    return Direction.Down;
                },
                _ => calls++);
        });
        Assert.True(seen);
        Assert.Equal(10, calls);
        Assert.Equal(Delivery.DeliveredUnverified, error.Disposition.Delivery);
    }

    [Fact]
    public void DeliveredVerifiedRequiresIntersectionSignal()
    {
        var viewport = R(0.0, 0.0, 100.0, 100.0);
        var hidden = new VisibilitySample(R(10.0, 200.0, 20.0, 20.0), false, viewport);
        var shown = new VisibilitySample(R(10.0, 40.0, 20.0, 20.0), false, viewport);
        Assert.False(ScrollLadder.VisibilityVerified(hidden));
        Assert.True(ScrollLadder.VisibilityVerified(shown));
        var scrolls = 0;
        var outcome = ScrollLadder.LadderJudgedFor(
            Deadline.After(5_000),
            () =>
            {
                if (scrolls < 2)
                {
                    Assert.False(ScrollLadder.VisibilityVerified(hidden));
                    return Direction.Down;
                }

                Assert.True(ScrollLadder.VisibilityVerified(shown));
                return null;
            },
            _ => scrolls++);
        Assert.Equal(DeliveryOutcome.DeliveredVerified, outcome);
    }

    [Fact]
    public void TenRungsExhaustedIsDeliveredUnverified()
    {
        var calls = 0;
        var error = Assert.Throws<AdapterError>(() =>
        {
            ScrollLadder.LadderJudgedFor(
                Deadline.After(5_000),
                () => Direction.Down,
                _ => calls++);
        });
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        Assert.Equal(DeliverySemantics.DeliveredUnverified(), error.Disposition);
        Assert.Equal(10, calls);
    }

    [Fact(Skip = "SEAM: csharp/src/AgentDesktop.Windows/Actions/ScrollLadder.cs has no RequireScrollDelivery; port the Ok(false) absence classifier")]
    public void AbsenceOkFalseDoesNotCountAsScrollDelivery()
    {
        Assert.Fail("SEAM: RequireScrollDelivery is not ported");
    }

    [Fact]
    public void ClassifiedWriteErrorAbortsWithoutFurtherScrolls()
    {
        foreach (var disposition in new[]
                 {
                     DeliverySemantics.NotDelivered(),
                     DeliverySemantics.DeliveryUncertain(),
                     DeliverySemantics.DeliveredUnverified(),
                 })
        {
            var calls = 0;
            var error = Assert.Throws<AdapterError>(() =>
            {
                ScrollLadder.LadderJudgedFor(
                    Deadline.After(5_000),
                    () => Direction.Down,
                    _ =>
                    {
                        calls++;
                        throw new AdapterError(ErrorCode.AppUnresponsive, "write failed")
                            .WithDisposition(disposition);
                    });
            });
            Assert.Equal(ErrorCode.AppUnresponsive, error.Code);
            Assert.Equal(1, calls);
            Assert.Equal(disposition, error.Disposition);
        }
    }

    [Fact]
    public void LaterScrollFailureAfterMutationIsDeliveredUnverified()
    {
        var calls = 0;
        var error = Assert.Throws<AdapterError>(() =>
        {
            ScrollLadder.LadderJudgedFor(
                Deadline.After(5_000),
                () => Direction.Down,
                _ =>
                {
                    var n = calls;
                    calls = n + 1;
                    if (n == 0)
                    {
                        return;
                    }

                    throw new AdapterError(
                            ErrorCode.ActionFailed,
                            "ScrollPattern is not available on the scroll ancestor")
                        .WithDisposition(DeliverySemantics.NotDelivered());
                });
        });
        Assert.Equal(2, calls);
        Assert.Equal(Delivery.DeliveredUnverified, error.Disposition.Delivery);
    }

    [Fact]
    public void DeadlineAtRungZeroStaysNotDelivered()
    {
        var expired = Deadline.After(1);
        Thread.Sleep(5);
        var calls = 0;
        var error = Assert.Throws<AdapterError>(() =>
        {
            ScrollLadder.LadderJudgedFor(
                expired,
                () => Direction.Down,
                _ => calls++);
        });
        Assert.Equal(ErrorCode.Timeout, error.Code);
        Assert.Equal(0, calls);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
    }

    [Fact]
    public void DeadlineAfterScrollsIsDeliveredUnverified()
    {
        var calls = 0;
        var shortDeadline = Deadline.After(30);
        var error = Assert.Throws<AdapterError>(() =>
        {
            ScrollLadder.LadderJudgedFor(
                shortDeadline,
                () => Direction.Down,
                _ =>
                {
                    calls++;
                    Thread.Sleep(20);
                });
        });
        Assert.True(calls >= 1);
        Assert.Equal(Delivery.DeliveredUnverified, error.Disposition.Delivery);
    }

    [Fact]
    public void SeamNoAncestorKeepsUnsupportedTerminal()
    {
        var fallback = new AdapterError(ErrorCode.ActionFailed, "ScrollIntoView is not available on this element")
            .WithDetails(new JsonObject
            {
                ["kind"] = "scroll_into_view_unsupported",
                ["complete"] = true,
                ["retryable"] = false,
            })
            .WithDisposition(DeliverySemantics.NotDelivered());
        var resolved = Assert.Throws<AdapterError>(() => ScrollLadder.ApplyLadderSeam(fallback, () => null));
        Assert.Same(fallback, resolved);
        Assert.Equal(fallback.Code, resolved.Code);
        Assert.Equal(fallback.Disposition, resolved.Disposition);
        Assert.Contains("scroll_into_view_unsupported", resolved.Details);
    }

    [Fact]
    public void SeamWithAncestorLaddersInsteadOfUnsupported()
    {
        var fallback = new AdapterError(ErrorCode.ActionFailed, "ScrollIntoView is not available on this element")
            .WithDisposition(DeliverySemantics.NotDelivered());
        var outcome = ScrollLadder.ApplyLadderSeam(fallback, () => DeliveryOutcome.DeliveredVerified);
        Assert.Equal(DeliveryOutcome.DeliveredVerified, outcome);
    }

    [Fact]
    public void MissingGeometryDoesNotInventDown()
    {
        var sample = new VisibilitySample(null, true, R(0.0, 0.0, 100.0, 100.0));
        var error = Assert.Throws<AdapterError>(() => ScrollLadder.DirectionAfterVisibilityMiss(sample));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
    }

    [Fact]
    public void ContainedButNotVisibleDoesNotInventDown()
    {
        var viewport = R(0.0, 0.0, 100.0, 100.0);
        var sample = new VisibilitySample(R(10.0, 10.0, 20.0, 20.0), true, viewport);
        Assert.False(ScrollLadder.VisibilityVerified(sample));
        Assert.Null(ScrollLadder.DirectionForVisibility(R(10.0, 10.0, 20.0, 20.0), viewport));
        var error = Assert.Throws<AdapterError>(() => ScrollLadder.DirectionAfterVisibilityMiss(sample));
        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
    }

    [Fact]
    public void PreciseScrollCentersTargetsAndClampsAtContentEdges()
    {
        Assert.Equal(60.0, ScrollLadder.CenteredScrollPercent(0.0, 20.0, 100.0, 240.0));
        Assert.Equal(0.0, ScrollLadder.CenteredScrollPercent(60.0, 20.0, 100.0, -240.0));
        Assert.Equal(100.0, ScrollLadder.CenteredScrollPercent(90.0, 20.0, 100.0, 240.0));
        var next = ScrollLadder.CenteredScrollPercent(0.0, 50.0, 611.0, 365.5);
        var movedY = 666.0 - next / 100.0 * 611.0;
        Assert.Null(ScrollLadder.DirectionForVisibility(
            R(84.0, movedY, 400.0, 150.0),
            R(48.0, 71.0, 867.0, 611.0)));
    }

    [Fact]
    public void InvalidScrollMetricsNeverProduceAWrite()
    {
        (double current, double size, double extent, double offset)[] cases =
        {
            (-1.0, 20.0, 100.0, 10.0),
            (0.0, 0.0, 100.0, 10.0),
            (0.0, 100.0, 100.0, 10.0),
            (0.0, 20.0, 0.0, 10.0),
            (double.NaN, 20.0, 100.0, 10.0),
            (0.0, 20.0, 100.0, double.PositiveInfinity),
        };
        foreach (var (current, size, extent, offset) in cases)
        {
            Assert.Throws<AdapterError>(() => ScrollLadder.CenteredScrollPercent(current, size, extent, offset));
        }
    }

    /// <summary>An element the provider reports on-screen, with real bounds, whose container publishes no viewport rect.
    /// Reading the absent viewport as hidden made scroll-to fail on every such element, so the unchanged geometry of an
    /// already-visible element was reported as a failed scroll.</summary>
    [Fact]
    public void AnUnresolvableViewportDoesNotMakeAnOnScreenElementInvisible()
    {
        var onScreenWithoutViewport = new VisibilitySample(R(10.0, 10.0, 120.0, 24.0), false, null);
        Assert.True(
            ScrollLadder.VisibilityVerified(onScreenWithoutViewport),
            "a container that publishes no viewport rect has declined to answer, not reported the element hidden");
    }

    [Fact]
    public void ASmallTargetMustBeWhollyInsideTheScrollViewport()
    {
        var sample = new VisibilitySample(R(10.0, 95.0, 20.0, 20.0), false, R(0.0, 0.0, 100.0, 100.0));
        Assert.False(
            ScrollLadder.VisibilityVerified(sample),
            "a clipped target midpoint cannot receive input");
    }
}
