using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Actions;

/// <summary>Ancestor-scroll ladder for ScrollTo when ScrollItemPattern is absent or leaves geometry unchanged (A19-7).
///
/// Direction comes from the target's fresh bounds against the nearest ScrollPattern-available ancestor viewport
/// (macOS direction_for_visibility parity). Visibility uses the ScrollIntoView predicate: IsOffscreen false,
/// positive-area bounds, and viewport containment when the target fits, because provider rects are unclipped (A18-2).
/// Mirrors crates/windows/src/actions/scroll_ladder.rs.</summary>
internal static class ScrollLadder
{
    internal const int MaxAncestorScrolls = 10;

    internal const string LadderScrollLabel = "ScrollPattern";

    /// <summary>Centers the target without page-sized oscillation; the view size is a percent of the content.</summary>
    internal static double CenteredScrollPercent(double current, double viewPercent, double viewportExtent, double offset)
    {
        if (!double.IsFinite(current)
            || current < 0.0
            || current > 100.0
            || !double.IsFinite(viewPercent)
            || viewPercent <= 0.0
            || viewPercent >= 100.0
            || !double.IsFinite(viewportExtent)
            || viewportExtent <= 0.0
            || !double.IsFinite(offset))
        {
            throw new AdapterError(ErrorCode.ActionFailed, "Invalid scroll viewport metrics")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        var scrollable = viewportExtent * (100.0 - viewPercent) / viewPercent;
        return Math.Clamp(current + offset / scrollable * 100.0, 0.0, 100.0);
    }

    /// <summary>On-screen, positive area, and, when a viewport rect resolves, intersecting it (A18-2).
    ///
    /// A viewport that does not resolve means the container did not report one, not that the element is hidden.
    /// Treating the two alike fails every element whose container keeps its viewport to itself: the provider reports
    /// the element on-screen with real bounds, the intersection test cannot run, visibility is declared unverified
    /// forever, and because an already-visible element makes ScrollIntoView a legitimate no-op, the unchanged geometry
    /// is then read as a failed scroll. Reproduced against File Explorer tree rows, which are on screen and returned
    /// ACTION_FAILED for exactly this reason.
    ///
    /// The viewport is therefore a strengthening check rather than a required one. Without it the provider's own
    /// IsOffscreen is the verdict.</summary>
    internal static bool VisibilityVerified(VisibilitySample sample)
    {
        if (sample.Bounds is not { } bounds)
        {
            return false;
        }

        if (!RectHasArea(bounds) || sample.Offscreen != false)
        {
            return false;
        }

        return sample.Viewport switch
        {
            { } viewport when bounds.Width <= viewport.Width && bounds.Height <= viewport.Height =>
                DirectionForVisibility(bounds, viewport) is null,
            { } viewport => Intersects(bounds, viewport),
            null => true,
        };
    }

    internal static bool Intersects(Rect left, Rect right) =>
        left.X < right.X + right.Width
        && left.X + left.Width > right.X
        && left.Y < right.Y + right.Height
        && left.Y + left.Height > right.Y;

    /// <summary>Pure geometry to scroll direction: vertical before horizontal, before-edge before after-edge.</summary>
    internal static Direction? DirectionForVisibility(Rect target, Rect viewport)
    {
        if (target.Y < viewport.Y)
        {
            return Direction.Up;
        }

        if (target.Y + target.Height > viewport.Y + viewport.Height)
        {
            return Direction.Down;
        }

        if (target.X < viewport.X)
        {
            return Direction.Left;
        }

        if (target.X + target.Width > viewport.X + viewport.Width)
        {
            return Direction.Right;
        }

        return null;
    }

    /// <summary>After a visibility miss, geometry must name a direction. Never invent Down.</summary>
    internal static Direction DirectionAfterVisibilityMiss(VisibilitySample sample)
    {
        if (sample.Bounds is not { } bounds || sample.Viewport is not { } viewport)
        {
            throw new AdapterError(
                    ErrorCode.ActionFailed,
                    "Could not determine ancestor scroll direction without target and viewport bounds")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return DirectionForVisibility(bounds, viewport)
            ?? throw new AdapterError(
                    ErrorCode.ActionFailed,
                    "Target is not visible but geometry does not indicate a scroll direction")
                .WithDisposition(DeliverySemantics.NotDelivered());
    }

    /// <summary>Runs the injected ladder: <paramref name="nextDirection"/> returns null when the target is in view
    /// (the live path folds the intersection predicate into that signal so an unclipped straddler does not verify,
    /// A18-2), and <paramref name="scrollOnce"/> performs one semantic scroll write. Serves as the unit-test seam and
    /// the live path.</summary>
    internal static DeliveryOutcome LadderJudgedFor(
        Deadline deadline,
        Func<Direction?> nextDirection,
        Action<Direction> scrollOnce)
    {
        var scrolled = false;
        for (var attempt = 0; attempt < MaxAncestorScrolls; attempt++)
        {
            Direction? direction;
            try
            {
                deadline.EnsureRemaining();
                direction = nextDirection();
            }
            catch (AdapterError error)
            {
                throw BudgetDisposition(scrolled, error);
            }

            if (direction is null)
            {
                return attempt == 0 ? DeliveryOutcome.SatisfiedNoDelivery : DeliveryOutcome.DeliveredVerified;
            }

            try
            {
                scrollOnce(direction.Value);
            }
            catch (AdapterError error)
            {
                throw BudgetDisposition(scrolled, error);
            }

            scrolled = true;
        }

        throw new AdapterError(
                ErrorCode.ActionFailed,
                "Semantic ancestor scrolling did not bring the target into view")
            .WithDisposition(DeliverySemantics.DeliveredUnverified());
    }

    /// <summary>Once a scroll write has landed, a later budget failure is delivered-unverified. Before that, an
    /// unknown or not-delivered failure is reported as not delivered.</summary>
    internal static AdapterError BudgetDisposition(bool scrolled, AdapterError error)
    {
        if (scrolled)
        {
            return error.WithDisposition(DeliverySemantics.DeliveredUnverified());
        }

        if (error.Disposition.Delivery is Delivery.Unknown or Delivery.NotDelivered)
        {
            return error.WithDisposition(DeliverySemantics.NotDelivered());
        }

        return error;
    }

    /// <summary>Ladder seam: a scrollable ancestor replaces the unsupported or not-delivered terminal. With no
    /// ancestor, the fallback terminal is returned unchanged.</summary>
    internal static DeliveryOutcome ApplyLadderSeam(AdapterError fallback, Func<DeliveryOutcome?> ladder) =>
        ladder() ?? throw fallback;

    /// <summary>Live ancestor ladder over the UIA raw-view walker. Returns null when no scrollable
    /// ancestor exists. The target's own process generation is corroborated before any write, so a
    /// recycled pid never scrolls a window it no longer owns.</summary>
    internal static DeliveryOutcome? AncestorLadder(Element element, Deadline deadline)
    {
        LiveRead.CorroborateVerifiedProcess(element);
        var viewport = Walker.NearestScrollViewport(element, deadline);
        if (viewport is null)
        {
            return null;
        }

        try
        {
            var viewportBounds = Walker.ViewportBounds(viewport);
            return LadderJudgedFor(deadline,
                () => DirectionWhenNotVisible(element, viewportBounds),
                direction => Scroll.ScrollSteps(viewport, direction, 1, deadline));
        }
        finally
        {
            viewport.Dispose();
        }
    }

    /// <summary>The direction the target still needs after a scroll, or null once it is in view. The
    /// re-read is what says the target arrived; the scroll write's own acknowledgement is not evidence.</summary>
    private static Direction? DirectionWhenNotVisible(Element element, Rect? viewport)
    {
        var sample = ObserveVisibilityDuringScroll(element, viewport);
        return VisibilityVerified(sample) ? null : DirectionAfterVisibilityMiss(sample);
    }

    private static VisibilitySample ObserveVisibilityDuringScroll(Element element, Rect? viewport)
    {
        var boundsField = element.CurrentBoundingRectangle();
        var offscreenField = element.CurrentIsOffscreen();
        return new VisibilitySample(
            boundsField.IsKnown() ? boundsField.Value : null,
            offscreenField.IsKnown() ? offscreenField.Value : null,
            viewport);
    }

    private static bool RectHasArea(Rect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width)
            && double.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0;
}
