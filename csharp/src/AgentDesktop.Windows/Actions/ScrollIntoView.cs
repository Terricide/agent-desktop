using System.Diagnostics;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>ScrollItemPattern.ScrollIntoView with delivery judged by observation.
///
/// When the pattern is unavailable or invoke leaves geometry unchanged, a scrollable ancestor runs the ladder (A19-7).
/// Provider rects are unclipped (A18-2), so verified visibility needs viewport intersection, which
/// <see cref="ScrollLadder.VisibilityVerified"/> applies. Invoke failures keep their tagged write failure; a completed
/// observation disposition outranks the classifier (A18).
/// Mirrors crates/windows/src/actions/scroll_into_view.rs.</summary>
internal static class ScrollIntoView
{
    internal const string ApiLabel = "ScrollItemPattern.ScrollIntoView";

    internal static readonly TimeSpan VerifyWindow = TimeSpan.FromMilliseconds(800);

    private const int PollSliceMs = 20;

    private const string Operation = "ScrollIntoView";

    /// <summary>Runs the pattern invoke, judges delivery by observation, and lets a scrollable ancestor ladder
    /// replace an unsupported or unchanged terminal.</summary>
    internal static (string Label, DeliveryOutcome Outcome) Run(Element element, Deadline deadline)
    {
        deadline.EnsureRemaining();
        LiveRead.CorroborateVerifiedProcess(element);
        return RunElement(element, deadline);
    }

    private static (string Label, DeliveryOutcome Outcome) RunElement(Element element, Deadline deadline)
    {
        if (element.CurrentProperty(PropertyIds.IsScrollItemPatternAvailable).Value is not true)
        {
            return LadderFromTerminal(UnsupportedError(), element, deadline);
        }

        var before = KnownBounds(element);
        var invoke = InvokeScrollIntoView(element);
        if (invoke.Empty)
        {
            return LadderFromTerminal(UnsupportedError(), element, deadline);
        }

        try
        {
            ScrollIntoViewJudgedFor(deadline, before, invoke.Failure, VerifyWindow, () => ObserveVisibility(element, deadline));
            return (ApiLabel, DeliveryOutcome.DeliveredVerified);
        }
        catch (AdapterError error) when (error.Disposition == DeliverySemantics.NotDelivered())
        {
            return LadderFromTerminal(error, element, deadline);
        }
    }

    private static (string Label, DeliveryOutcome Outcome) LadderFromTerminal(
        AdapterError fallback,
        Element element,
        Deadline deadline)
    {
        var outcome = ScrollLadder.ApplyLadderSeam(fallback, () => ScrollLadder.AncestorLadder(element, deadline));
        return (ScrollLadder.LadderScrollLabel, outcome);
    }

    /// <summary>Polls visibility until it verifies or the local window (capped by the caller deadline) expires.
    /// Verification failure after a completed invoke is classified by <see cref="FinishObservation"/>.</summary>
    internal static void ScrollIntoViewJudgedFor(
        Deadline deadline,
        Rect? before,
        WriteFailure? invokeFailure,
        TimeSpan verifyWindow,
        Func<VisibilitySample> observe)
    {
        var localWatch = Stopwatch.StartNew();
        Rect? lastBounds = null;
        while (true)
        {
            VisibilitySample sample;
            try
            {
                sample = observe();
            }
            catch (AdapterError error)
            {
                throw Chain.AfterDelivery(error);
            }

            lastBounds = sample.Bounds;
            if (ScrollLadder.VisibilityVerified(sample))
            {
                return;
            }

            if (deadline.IsExpired())
            {
                throw Chain.AfterDelivery(deadline.TimeoutError().WithDetails(new JsonObject
                {
                    ["verification"] = "scroll_visibility_not_observed",
                }));
            }

            if (localWatch.Elapsed >= verifyWindow)
            {
                FinishObservation(before, lastBounds, invokeFailure);
            }

            Thread.Sleep((int)Math.Max(1, Math.Min(PollSliceMs, deadline.RemainingMs())));
        }
    }

    /// <summary>Throws the observation disposition for a completed invoke whose visibility never verified.</summary>
    internal static void FinishObservation(Rect? before, Rect? after, WriteFailure? invokeFailure)
    {
        if (CompletedBounds(after) is not { } settled)
        {
            throw UnverifiedError("ScrollIntoView completed without an observable after-state", invokeFailure);
        }

        if (CompletedBounds(before) is not { } start)
        {
            throw UnverifiedError("ScrollIntoView completed without a comparable before-state", invokeFailure);
        }

        if (!ScrollEffectObserved(start, settled))
        {
            throw NotDeliveredError(invokeFailure);
        }

        throw UnverifiedError("ScrollIntoView moved the target but visibility was not verified", invokeFailure);
    }

    /// <summary>Geometry changed when its canonical hash differs; a rectangle that fails validation never matches.</summary>
    internal static bool ScrollEffectObserved(Rect before, Rect after) =>
        before.BoundsHash() != after.BoundsHash();

    internal static AdapterError UnsupportedError() =>
        new AdapterError(ErrorCode.ActionFailed, "ScrollIntoView is not available on this element")
            .WithSuggestion(
                "The element does not expose ScrollItemPattern; scroll a containing viewport first, or target a scroll-item control")
            .WithDetails(new JsonObject
            {
                ["kind"] = "scroll_into_view_unsupported",
                ["complete"] = true,
                ["retryable"] = false,
            })
            .WithDisposition(DeliverySemantics.NotDelivered());

    private static AdapterError NotDeliveredError(WriteFailure? invokeFailure) =>
        OverlayInvokeClassification(
            new AdapterError(ErrorCode.ActionFailed, "ScrollIntoView did not change target geometry")
                .WithDisposition(DeliverySemantics.NotDelivered()),
            invokeFailure);

    private static AdapterError UnverifiedError(string message, WriteFailure? invokeFailure) =>
        OverlayInvokeClassification(
            new AdapterError(ErrorCode.ActionFailed, message)
                .WithDisposition(DeliverySemantics.DeliveredUnverified()),
            invokeFailure);

    /// <summary>The observation disposition stands over transport-uncertain invoke HRESULTs (A18). A write the
    /// classifier refuses or maps to absent leaves the observation unchanged.</summary>
    private static AdapterError OverlayInvokeClassification(AdapterError observation, WriteFailure? invokeFailure)
    {
        if (invokeFailure is not { } failure)
        {
            return observation;
        }

        try
        {
            Mutation.ClassifyMutation(Operation, ApiLabel, failure);
            return observation;
        }
        catch (AdapterError classified)
        {
            var error = new AdapterError(classified.Code, classified.Message).WithDisposition(observation.Disposition);
            if (classified.PlatformDetail is { } detail)
            {
                error = error.WithPlatformDetail(detail);
            }

            if (classified.Suggestion is { } suggestion)
            {
                error = error.WithSuggestion(suggestion);
            }

            if (observation.Details is { } details)
            {
                error = error.WithDetails(details);
            }

            return error;
        }
    }

    private static (bool Empty, WriteFailure? Failure) InvokeScrollIntoView(Element element)
    {
        var hr = element.CurrentPattern(Slots.PatternScrollItem, out var pattern);
        if (hr < 0 && !UiaFailure.IsPatternAbsent(hr))
        {
            Calls.Release(pattern);
            return (false, WriteFailure.FromHresult(hr));
        }

        if (hr < 0 || pattern == IntPtr.Zero)
        {
            Calls.Release(pattern);
            return (true, null);
        }

        try
        {
            var invokeHr = Calls.CallVoid(pattern, Slots.ScrollItemPatternScrollIntoView);
            return invokeHr < 0
                ? (false, WriteFailure.FromHresult(invokeHr))
                : (false, (WriteFailure?)null);
        }
        finally
        {
            Calls.Release(pattern);
        }
    }

    private static VisibilitySample ObserveVisibility(Element element, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var boundsField = element.CurrentBoundingRectangle();
        var offscreenField = element.CurrentIsOffscreen();
        Rect? bounds = boundsField.IsKnown() ? boundsField.Value : null;
        bool? offscreen = offscreenField.IsKnown() ? offscreenField.Value : null;
        return new VisibilitySample(bounds, offscreen, ScrollViewportBounds(element));
    }

    private static Rect? KnownBounds(Element element) =>
        element.CurrentBoundingRectangle() is { Kind: LocatorFieldKind.Known, Value: { } rect } ? rect : null;

    private static Rect? CompletedBounds(Rect? bounds) =>
        bounds is { Width: > 0, Height: > 0 } rect ? rect : null;

    private static Rect? ScrollViewportBounds(Element element) =>
        throw AdapterError.NotSupported("nearest_scroll_viewport");
}
