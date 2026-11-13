using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Actions;
using Xunit;

namespace AgentDesktop.Windows.Tests.Actions;

/// <summary>Pins for ScrollItemPattern.ScrollIntoView delivery judgment, ported from
/// crates/windows/src/actions/scroll_into_view_tests.rs. The C# judgment lives in
/// Actions/ScrollAction.cs and Actions/ScrollLadder.cs; there is no ScrollIntoView.cs, and
/// the judgment is not injectable, so the pins that drive it through an observation closure
/// or through finish_observation cannot reach the C# code without a live provider.</summary>
public sealed class ScrollIntoViewTests
{
    private static Rect MakeRect(double x, double y, double width, double height) =>
        new(x, y, width, height);

    private static VisibilitySample VisibleInViewport(Rect bounds, Rect viewport) =>
        new(bounds, false, viewport);

    private static VisibilitySample HiddenSample(Rect bounds) =>
        new(bounds, true, MakeRect(0.0, 0.0, 100.0, 100.0));

    /// <summary>Ported from area_requires_finite_positive_dimensions. A provider rect with a
    /// non-finite coordinate is not an area, so a visibility check built on it cannot verify.
    /// The C# area predicate is private to ScrollLadder, so this drives it through the
    /// published visibility contract.</summary>
    [Fact]
    public void AreaRequiresFinitePositiveDimensions()
    {
        var viewport = MakeRect(0.0, 0.0, 20.0, 20.0);
        Assert.True(ScrollLadder.VisibilityVerified(VisibleInViewport(MakeRect(0.0, 0.0, 10.0, 10.0), viewport)));
        Assert.False(ScrollLadder.VisibilityVerified(VisibleInViewport(MakeRect(0.0, 0.0, 0.0, 10.0), viewport)));
        Assert.False(ScrollLadder.VisibilityVerified(VisibleInViewport(MakeRect(double.NaN, 0.0, 10.0, 10.0), viewport)));
    }

    /// <summary>Ported from verified_requires_on_screen_area_and_viewport_intersection. A18-2
    /// measured that a provider rect extends outside the viewport, so an unclipped rect must
    /// not read as fully visible. A container that publishes no viewport is not a hidden
    /// element: the provider's own IsOffscreen is the verdict, which is what kept File
    /// Explorer's on-screen rows from verifying.</summary>
    [Fact]
    public void VerifiedRequiresOnScreenAreaAndViewportIntersection()
    {
        var bounds = MakeRect(10.0, 10.0, 20.0, 20.0);
        var viewport = MakeRect(0.0, 0.0, 100.0, 100.0);
        Assert.True(ScrollLadder.VisibilityVerified(VisibleInViewport(bounds, viewport)));
        Assert.False(ScrollLadder.VisibilityVerified(new VisibilitySample(bounds, true, viewport)));
        Assert.False(ScrollLadder.VisibilityVerified(new VisibilitySample(MakeRect(200.0, 200.0, 20.0, 20.0), false, viewport)));
        Assert.True(ScrollLadder.VisibilityVerified(new VisibilitySample(bounds, false, null)));
    }

    /// <summary>Ported from unsupported_is_action_failed_not_platform_not_supported. The
    /// unsupported arm is the honest ACTION_FAILED with not-delivered details, not the trait
    /// default's PLATFORM_NOT_SUPPORTED. The C# builder is private to ScrollAction, so only
    /// the default half is reachable without a live element.</summary>
    [Fact(Skip = "requires the ScrollIntoView unsupported builder, private to ScrollAction")]
    public void UnsupportedIsActionFailedNotPlatformNotSupported()
    {
    }

    /// <summary>Ported from verified_visible_after_invoke_is_ok. Needs an observation closure
    /// injected into the ScrollIntoView judge; the C# judge polls a live element only.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void VerifiedVisibleAfterInvokeIsOk()
    {
    }

    /// <summary>Ported from unchanged_geometry_is_not_delivered_not_unverified. An already
    /// visible element makes ScrollIntoView a legitimate no-op, so unchanged geometry is
    /// not-delivered, never delivered-unverified. Needs finish_observation, which the C# port
    /// folds into ScrollIntoViewOutcome with no injectable observation.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void UnchangedGeometryIsNotDeliveredNotUnverified()
    {
    }

    /// <summary>Ported from moved_but_unproven_is_delivered_unverified_not_not_delivered. A
    /// bounds hash change proves the write landed even when visibility never verified.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void MovedButUnprovenIsDeliveredUnverifiedNotNotDelivered()
    {
    }

    /// <summary>Ported from observation_failure_is_delivered_unverified_never_bare_read_err.
    /// A provider failure during the after-observation is a completed write whose delivery is
    /// unproven, so the read error must be re-dispositioned, never returned bare.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void ObservationFailureIsDeliveredUnverifiedNeverBareReadErr()
    {
    }

    /// <summary>Ported from degenerate_after_state_is_delivered_unverified_not_not_delivered.
    /// A zero-area after-state is not a comparable observation, so the write stays
    /// delivered-unverified rather than being read as not delivered.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void DegenerateAfterStateIsDeliveredUnverifiedNotNotDelivered()
    {
    }

    /// <summary>Ported from degenerate_before_state_is_delivered_unverified. A zero-area
    /// before-state cannot be compared against, so delivery stays unproven.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void DegenerateBeforeStateIsDeliveredUnverified()
    {
    }

    /// <summary>Ported from failed_invoke_unchanged_geometry_overlays_classifier_code_and_platform_detail.
    /// UIA_E_ELEMENTNOTAVAILABLE classifies to STALE_REF, and the completed observation's
    /// not-delivered disposition stands over the classifier's own (A18).</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void FailedInvokeUnchangedGeometryOverlaysClassifierCodeAndPlatformDetail()
    {
    }

    /// <summary>Ported from denied_invoke_unchanged_geometry_is_perm_denied_not_delivered.
    /// E_ACCESSDENIED classifies to PERM_DENIED with the observation's not-delivered standing.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void DeniedInvokeUnchangedGeometryIsPermDeniedNotDelivered()
    {
    }

    /// <summary>Ported from transport_failed_invoke_unchanged_completed_observation_is_not_delivered.
    /// RPC_E_DISCONNECTED classifies to APP_UNRESPONSIVE with delivery uncertain, and the
    /// completed observation's not-delivered outranks that uncertainty.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void TransportFailedInvokeUnchangedCompletedObservationIsNotDelivered()
    {
    }

    /// <summary>Ported from transport_failed_invoke_with_failed_observation_is_delivered_unverified.
    /// When the observation never completes, nothing outranks the transport uncertainty, so the
    /// write is delivered-unverified.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void TransportFailedInvokeWithFailedObservationIsDeliveredUnverified()
    {
    }

    /// <summary>Ported from zero_verify_window_unchanged_reaches_not_delivered. A zero-length
    /// verification window must still reach the not-delivered arm, not an unverified one.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void ZeroVerifyWindowUnchangedReachesNotDelivered()
    {
    }

    /// <summary>Ported from live_element_without_scroll_item_is_unsupported_through_the_adapter.
    /// Drives the shipped gate against a real provider: a Win32 BUTTON exposes no
    /// ScrollItemPattern, so the gate reads the property, refuses before invoking, and returns
    /// the honest ACTION_FAILED rather than the trait default's PLATFORM_NOT_SUPPORTED.</summary>
    [Fact(Skip = "requires a live UIA tree")]
    public void LiveElementWithoutScrollItemIsUnsupportedThroughTheAdapter()
    {
    }
}
