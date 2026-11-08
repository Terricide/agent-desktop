using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>ScrollPattern directional scroll with per-axis honesty and ScrollItemPattern
/// ScrollIntoView. Mirrors crates/windows/src/actions/scroll.rs and scroll_into_view.rs.</summary>
internal static class ScrollAction
{
    internal const string ScrollLabel = "ScrollPattern.Scroll";

    internal const string ScrollIntoViewLabel = "ScrollItemPattern.ScrollIntoView";

    private const uint MaxScrollAmount = 1_000;

    private const int ScrollAmountSmallDecrement = 1;

    private const int ScrollAmountNoAmount = 2;

    private const int ScrollAmountSmallIncrement = 4;

    internal static List<ActionStep> ScrollSteps(
        Element element,
        Direction direction,
        uint amount,
        Deadline deadline)
    {
        ValidateScrollAmount(amount);
        var scrollAvailable = element.CurrentProperty(PropertyIds.IsScrollPatternAvailable).Value is true;
        var pattern = IntPtr.Zero;
        if (scrollAvailable)
        {
            element.CurrentPattern(Slots.PatternScroll, out pattern);
        }

        try
        {
            var axis = AxisName(direction);
            var axisScrollable = pattern != IntPtr.Zero && AxisIsScrollable(pattern, direction);
            var beforePercent = pattern != IntPtr.Zero ? ReadScrollPercent(pattern, direction) : null;
            var beforeBounds = KnownBounds(element);
            var (horizontal, vertical) = ScrollAmounts(direction);

            return ScrollJudgedFor(
                deadline,
                scrollAvailable,
                axisScrollable,
                axis,
                amount,
                () =>
                {
                    if (pattern == IntPtr.Zero)
                    {
                        return;
                    }

                    var hr = Calls.CallWithTwoInts(pattern, Slots.ScrollPatternScroll, horizontal, vertical);
                    var delivered = hr >= 0 || Chain.ClassifyWriteOrNotDelivered(hr, "Scroll", ScrollLabel);
                    if (!delivered)
                    {
                        throw AxisNotScrollable(axis);
                    }
                },
                () =>
                {
                    var afterPercent = pattern != IntPtr.Zero ? ReadScrollPercent(pattern, direction) : null;
                    var afterBounds = KnownBounds(element);
                    return ScrollEffectVerified(beforePercent, afterPercent, beforeBounds, afterBounds);
                });
        }
        finally
        {
            Calls.Release(pattern);
        }
    }

    internal static List<ActionStep> ScrollJudgedFor(
        Deadline deadline,
        bool scrollAvailable,
        bool axisScrollable,
        string axisName,
        uint amount,
        System.Action scrollOnce,
        Func<bool> observeEffect)
    {
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        ValidateScrollAmount(amount);
        if (!scrollAvailable)
        {
            throw ScrollUnavailable();
        }

        if (!axisScrollable)
        {
            throw AxisNotScrollable(axisName);
        }

        for (uint completed = 0; completed < amount; completed++)
        {
            if (deadline.IsExpired())
            {
                var expiry = WindowRoot.DeadlineExceeded(deadline);
                throw completed == 0 ? expiry : PartialScrollError(expiry, completed, amount);
            }

            try
            {
                scrollOnce();
            }
            catch (AdapterError error)
            {
                throw completed == 0 ? error : PartialScrollError(error, completed, amount);
            }
        }

        var verified = observeEffect();
        return new List<ActionStep> { Chain.BuildStep(ScrollLabel, Chain.FromDelivery(true, verified)) };
    }

    internal static (string Label, DeliveryOutcome Outcome) ScrollIntoViewOutcome(Element element, Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        if (element.CurrentProperty(PropertyIds.IsScrollItemPatternAvailable).Value is not true)
        {
            throw ScrollIntoViewUnsupported();
        }

        var before = KnownBounds(element);
        var hr = element.CurrentPattern(Slots.PatternScrollItem, out var pattern);
        if (hr < 0 || pattern == IntPtr.Zero)
        {
            Calls.Release(pattern);
            if (UiaFailure.IsPatternAbsent(hr))
            {
                throw ScrollIntoViewUnsupported();
            }

            throw UiaFailure.WriteError(hr, "get_pattern", ScrollIntoViewLabel);
        }

        try
        {
            var invokeHr = Calls.CallVoid(pattern, Slots.ScrollItemPatternScrollIntoView);
            if (invokeHr < 0)
            {
                if (UiaFailure.IsPatternAbsent(invokeHr))
                {
                    throw ScrollIntoViewUnsupported();
                }

                throw UiaFailure.WriteError(invokeHr, "ScrollIntoView", ScrollIntoViewLabel);
            }
        }
        finally
        {
            Calls.Release(pattern);
        }

        Rect? lastBounds = null;
        var verified = Chain.PollVerify(deadline, 800, 20, () =>
        {
            lastBounds = KnownBounds(element);
            return element.CurrentIsOffscreen().Value == false
                && lastBounds is { Width: > 0, Height: > 0 };
        });

        if (verified)
        {
            return (ScrollIntoViewLabel, DeliveryOutcome.DeliveredVerified);
        }

        if (before is { Width: > 0, Height: > 0 } b
            && lastBounds is { Width: > 0, Height: > 0 } a
            && b == a)
        {
            throw new AdapterError(ErrorCode.ActionFailed, "ScrollIntoView did not change target geometry")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        throw new AdapterError(ErrorCode.ActionFailed, "ScrollIntoView moved the target but visibility was not verified")
            .WithDisposition(DeliverySemantics.DeliveredUnverified());
    }

    internal static bool ScrollEffectVerified(
        double? beforePercent,
        double? afterPercent,
        Rect? beforeBounds,
        Rect? afterBounds) =>
        beforePercent is { } bp && afterPercent is { } ap && Math.Abs(ap - bp) > double.Epsilon
            ? true
            : beforeBounds is { } bb && afterBounds is { } ab && bb != ab;

    internal static (int Horizontal, int Vertical) ScrollAmounts(Direction direction) => direction switch
    {
        Direction.Down => (ScrollAmountNoAmount, ScrollAmountSmallIncrement),
        Direction.Up => (ScrollAmountNoAmount, ScrollAmountSmallDecrement),
        Direction.Right => (ScrollAmountSmallIncrement, ScrollAmountNoAmount),
        Direction.Left => (ScrollAmountSmallDecrement, ScrollAmountNoAmount),
        _ => (ScrollAmountNoAmount, ScrollAmountSmallIncrement),
    };

    internal static string AxisName(Direction direction) =>
        direction is Direction.Up or Direction.Down ? "vertical" : "horizontal";

    private static bool AxisIsScrollable(IntPtr pattern, Direction direction)
    {
        var slot = direction is Direction.Up or Direction.Down
            ? Slots.ScrollPatternGetCurrentVerticallyScrollable
            : Slots.ScrollPatternGetCurrentHorizontallyScrollable;
        return Calls.CallIntOut(pattern, slot, out var flag) >= 0 && flag != 0;
    }

    private static double? ReadScrollPercent(IntPtr pattern, Direction direction)
    {
        var slot = direction is Direction.Up or Direction.Down
            ? Slots.ScrollPatternGetCurrentVerticalScrollPercent
            : Slots.ScrollPatternGetCurrentHorizontalScrollPercent;
        return Calls.CallDoubleOut(pattern, slot, out var percent) >= 0 ? percent : null;
    }

    private static Rect? KnownBounds(Element element) =>
        element.CurrentBoundingRectangle() is { Kind: LocatorFieldKind.Known, Value: { } rect } ? rect : null;

    private static void ValidateScrollAmount(uint amount)
    {
        if (amount == 0 || amount > MaxScrollAmount)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, $"Scroll amount must be between 1 and {MaxScrollAmount}");
        }
    }

    private static AdapterError ScrollUnavailable() =>
        new AdapterError(ErrorCode.ActionFailed, "ScrollPattern is not available on this element")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithSuggestion("Target an element that advertises Scroll, or scroll a containing viewport.");

    private static AdapterError AxisNotScrollable(string axis) =>
        new AdapterError(ErrorCode.ActionFailed, $"The {axis} axis is not scrollable")
            .WithDisposition(DeliverySemantics.NotDelivered())
            .WithDetails(new JsonObject { ["kind"] = "axis_not_scrollable", ["axis"] = axis })
            .WithSuggestion("Scroll along the axis the control exposes, or target a different container.");

    private static AdapterError PartialScrollError(AdapterError cause, uint completed, uint requested) =>
        new AdapterError(
            cause.Code,
            $"ScrollPattern.Scroll stopped after {completed} of {requested} requested scroll steps")
            .WithPlatformDetail(cause.Message)
            .WithDetails(new JsonObject
            {
                ["action_may_have_completed"] = true,
                ["completed_steps"] = completed,
                ["requested_steps"] = requested,
            })
            .WithDisposition(DeliverySemantics.DeliveredUnverified())
            .WithSuggestion("Inspect the current scroll position before deciding whether to retry.");

    private static AdapterError ScrollIntoViewUnsupported() =>
        new AdapterError(ErrorCode.ActionFailed, "ScrollIntoView is not available on this element")
            .WithSuggestion("The element does not expose ScrollItemPattern; scroll a containing viewport first, or target a scroll-item control")
            .WithDetails(new JsonObject
            {
                ["kind"] = "scroll_into_view_unsupported",
                ["complete"] = true,
                ["retryable"] = false,
            })
            .WithDisposition(DeliverySemantics.NotDelivered());
}
