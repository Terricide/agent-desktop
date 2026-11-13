using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Actions;

/// <summary>
/// ScrollPattern directional scroll with per-axis honesty (A19-8).
/// Gates ScrollAvailable on the element, maps direction to SmallIncrement or
/// SmallDecrement on one axis, invokes the scroll amount times under the
/// deadline, and verifies by scroll-percent delta or bounds change.
/// </summary>
internal static class Scroll
{
    internal const string Label = "ScrollPattern.Scroll";

    internal const uint MaxScrollAmount = 1_000;

    /// <summary>Rust f64::EPSILON; double.Epsilon in C# is the smallest subnormal and would not match.</summary>
    internal const double MachineEpsilon = 2.220446049250313e-16;

    /// <summary>UIA ScrollAmount values, per the UI Automation specification.</summary>
    internal enum ScrollAmount
    {
        LargeDecrement = 0,
        SmallDecrement = 1,
        NoAmount = 2,
        SmallIncrement = 3,
        LargeIncrement = 4,
    }

    /// <summary>Scroll pattern surface the directional logic needs. Live UIA binding supplies it.</summary>
    internal interface IScrollPattern
    {
        bool IsVerticallyScrollable();

        bool IsHorizontallyScrollable();

        double VerticalScrollPercent();

        double HorizontalScrollPercent();

        void Scroll(ScrollAmount horizontal, ScrollAmount vertical);
    }

    /// <summary>Injected scroll plan shared by the live path and the unit-test seam.</summary>
    internal sealed record Plan(bool ScrollAvailable, bool AxisScrollable, string AxisName, uint Amount);

    internal static (ScrollAmount Horizontal, ScrollAmount Vertical) AmountsFor(Direction direction)
    {
        return direction switch
        {
            Direction.Down => (ScrollAmount.NoAmount, ScrollAmount.SmallIncrement),
            Direction.Up => (ScrollAmount.NoAmount, ScrollAmount.SmallDecrement),
            Direction.Right => (ScrollAmount.SmallIncrement, ScrollAmount.NoAmount),
            Direction.Left => (ScrollAmount.SmallDecrement, ScrollAmount.NoAmount),
            _ => throw new AdapterError(ErrorCode.InvalidArgs, $"Unsupported scroll direction {direction}"),
        };
    }

    internal static string AxisName(Direction direction)
    {
        return direction switch
        {
            Direction.Up or Direction.Down => "vertical",
            Direction.Left or Direction.Right => "horizontal",
            _ => throw new AdapterError(ErrorCode.InvalidArgs, $"Unsupported scroll direction {direction}"),
        };
    }

    /// <summary>Percent-delta wins when both samples exist; otherwise bounds change.</summary>
    internal static bool ScrollEffectVerified(double? beforePercent, double? afterPercent, Rect? beforeBounds, Rect? afterBounds)
    {
        if (beforePercent is { } before && afterPercent is { } after && Math.Abs(after - before) > MachineEpsilon)
        {
            return true;
        }

        if (beforeBounds is { } beforeRect && afterBounds is { } afterRect)
        {
            return beforeRect.BoundsHash() != afterRect.BoundsHash();
        }

        return false;
    }

    internal static IReadOnlyList<ActionStep> ScrollSteps(Element element, Direction direction, uint amount, Deadline deadline)
    {
        ValidateAmount(amount);
        var scrollAvailable = PropertyReader.ReadOne(element, TreeProperty.ScrollAvailable).Flag() == true;
        var pattern = scrollAvailable ? TryAcquirePattern(element) : null;
        var axis = AxisName(direction);
        var axisScrollable = pattern is not null && AxisIsScrollable(pattern, direction);
        var beforePercent = pattern is null ? null : ReadPercent(pattern, direction);
        var beforeBounds = KnownBounds(element);
        var (horizontal, vertical) = AmountsFor(direction);

        void ScrollOnce()
        {
            if (pattern is null)
            {
                return;
            }

            bool delivered;
            try
            {
                pattern.Scroll(horizontal, vertical);
                delivered = Mutation.ClassifySuccess();
            }
            catch (COMException error)
            {
                delivered = Mutation.ClassifyWrite("Scroll", Label, error);
            }

            if (!delivered)
            {
                throw AxisNotScrollable(axis);
            }
        }

        bool ObserveEffect()
        {
            var afterPercent = pattern is null ? null : ReadPercent(pattern, direction);
            var afterBounds = KnownBounds(element);
            return ScrollEffectVerified(beforePercent, afterPercent, beforeBounds, afterBounds);
        }

        return ScrollJudgedFor(deadline, new Plan(scrollAvailable, axisScrollable, axis, amount), ScrollOnce, ObserveEffect);
    }

    internal static IReadOnlyList<ActionStep> ScrollJudgedFor(Deadline deadline, Plan plan, global::System.Action scrollOnce, global::System.Func<bool> observeEffect)
    {
        deadline.EnsureRemaining();
        ValidateAmount(plan.Amount);
        if (!plan.ScrollAvailable)
        {
            throw ScrollUnavailable();
        }

        if (!plan.AxisScrollable)
        {
            throw AxisNotScrollable(plan.AxisName);
        }

        for (uint completed = 0; completed < plan.Amount; completed++)
        {
            try
            {
                deadline.EnsureRemaining();
                scrollOnce();
            }
            catch (AdapterError error) when (completed > 0)
            {
                throw PartialScrollError(error, completed, plan.Amount);
            }
        }

        var verified = observeEffect();
        return [Chain.BuildStep(Label, DeliveryOutcomeRules.FromDelivery(true, verified))];
    }

    /// <summary>The one bounds-or-null read shared with ScrollIntoView's before/after comparison.</summary>
    internal static Rect? KnownBounds(Element element)
    {
        var field = PropertyReader.ReadOne(element, TreeProperty.BoundingRectangle).Bounds();
        return field.IsKnown() ? field.Value : null;
    }

    private static IScrollPattern? TryAcquirePattern(Element element)
    {
        try
        {
            return element.GetScrollPattern();
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool AxisIsScrollable(IScrollPattern pattern, Direction direction)
    {
        try
        {
            return direction switch
            {
                Direction.Up or Direction.Down => pattern.IsVerticallyScrollable(),
                Direction.Left or Direction.Right => pattern.IsHorizontallyScrollable(),
                _ => false,
            };
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static double? ReadPercent(IScrollPattern pattern, Direction direction)
    {
        try
        {
            return direction switch
            {
                Direction.Up or Direction.Down => pattern.VerticalScrollPercent(),
                Direction.Left or Direction.Right => pattern.HorizontalScrollPercent(),
                _ => null,
            };
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void ValidateAmount(uint amount)
    {
        if (amount == 0 || amount > MaxScrollAmount)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, $"Scroll amount must be between 1 and {MaxScrollAmount}");
        }
    }

    private static AdapterError ScrollUnavailable()
    {
        return new AdapterError(ErrorCode.ActionFailed, "ScrollPattern is not available on this element")
            .WithDisposition(Disposition.NotDelivered())
            .WithSuggestion("Target an element that advertises Scroll, or scroll a containing viewport.");
    }

    private static AdapterError AxisNotScrollable(string axis)
    {
        var details = new JsonObject
        {
            ["kind"] = "axis_not_scrollable",
            ["axis"] = axis,
        };
        return new AdapterError(ErrorCode.ActionFailed, $"The {axis} axis is not scrollable")
            .WithDisposition(Disposition.NotDelivered())
            .WithDetails(details)
            .WithSuggestion("Scroll along the axis the control exposes, or target a different container.");
    }

    /// <summary>
    /// Re-frames a mid-run failure as a partial scroll without replacing what went wrong.
    /// The steps already delivered change the disposition (the caller must re-read before retrying) but
    /// not the cause. A budget that ran out is retryable with a longer one; a control that refused the
    /// step is not. The original code is kept and its message carried in the platform detail.
    /// </summary>
    private static AdapterError PartialScrollError(AdapterError cause, uint completed, uint requested)
    {
        var details = new JsonObject
        {
            ["action_may_have_completed"] = true,
            ["completed_steps"] = completed,
            ["requested_steps"] = requested,
        };
        return new AdapterError(cause.Code, $"ScrollPattern.Scroll stopped after {completed} of {requested} requested scroll steps")
            .WithPlatformDetail(cause.Message)
            .WithDetails(details)
            .WithDisposition(Disposition.DeliveredUnverified())
            .WithSuggestion("Inspect the current scroll position before deciding whether to retry.");
    }
}
