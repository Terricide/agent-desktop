using System.Globalization;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Actions;

/// <summary>ValuePattern and RangeValuePattern writes for SetValue and Clear, with IsPassword-gated
/// verification readback. Mirrors crates/windows/src/actions/value_write.rs.</summary>
internal static class ValueWrite
{
    internal const string ValueLabel = "ValuePattern.SetValue";

    internal const string RangeLabel = "RangeValuePattern.SetValue";

    internal static readonly ChainDef ValueWriteChain = new(
        "Refresh the snapshot and retry, or target a writable Value or RangeValue control.",
        false);

    internal static readonly ChainDef ClearChain = new(
        "Refresh the snapshot and retry, or target a writable Value control.",
        false);

    internal static List<ActionStep> SetValueSteps(
        Element element,
        string value,
        InteractionPolicy policy,
        Deadline deadline)
    {
        var parsed = ParseFiniteDouble(value);
        return SetValueJudgedFor(
            deadline,
            policy,
            value,
            ValueWritable(element),
            RangeAvailable(element),
            () => InvokeValueSet(element, value),
            () => parsed is { } target ? InvokeRangeSet(element, target) : DeliveryOutcome.NotDelivered);
    }

    internal static List<ActionStep> ClearSteps(
        Element element,
        InteractionPolicy policy,
        Deadline deadline) =>
        ClearJudgedFor(deadline, policy, ValueWritable(element), () => InvokeValueSet(element, ""));

    internal static List<ActionStep> SetValueJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        string value,
        bool valueWritable,
        bool rangeAvailable,
        Func<DeliveryOutcome> valueWrite,
        Func<DeliveryOutcome> rangeWrite)
    {
        var parsed = ParseFiniteDouble(value);
        try
        {
            return Chain.Execute(
                deadline,
                ValueWriteChain,
                policy,
                new[]
                {
                    new ChainRung(ValueLabel, false, Chain.Gated(valueWritable, valueWrite)),
                    new ChainRung(RangeLabel, false, Chain.Gated(rangeAvailable && parsed.HasValue, rangeWrite)),
                });
        }
        catch (AdapterError error)
        {
            throw AttachValueChars(error, value);
        }
    }

    internal static List<ActionStep> ClearJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        bool valueWritable,
        Func<DeliveryOutcome> valueWrite)
    {
        try
        {
            return Chain.Execute(
                deadline,
                ClearChain,
                policy,
                new[] { new ChainRung(ValueLabel, false, Chain.Gated(valueWritable, valueWrite)) });
        }
        catch (AdapterError error)
        {
            throw AttachValueChars(error, "");
        }
    }

    internal static double? ParseFiniteDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number
            : null;

    internal static bool? GatedPatternValueEquals(Element element, string expected)
    {
        if (NodeProperties.IsLivePassword(element))
        {
            return null;
        }

        var hr = element.CurrentPattern(Slots.PatternValue, out var pattern);
        if (hr < 0 || pattern == IntPtr.Zero)
        {
            Calls.Release(pattern);
            throw ReadFailed("ValuePattern.get_value", hr);
        }

        try
        {
            var valueHr = Calls.CallStringOut(pattern, Slots.ValuePatternGetCurrentValue, out var text);
            if (valueHr < 0)
            {
                throw ReadFailed("ValuePattern.get_value", valueHr);
            }

            return (text ?? "") == expected;
        }
        finally
        {
            Calls.Release(pattern);
        }
    }

    private static DeliveryOutcome InvokeValueSet(Element element, string value)
    {
        var hr = element.CurrentPattern(Slots.PatternValue, out var pattern);
        if (hr < 0 || pattern == IntPtr.Zero)
        {
            Calls.Release(pattern);
            return Chain.FromDelivery(Chain.ClassifyWriteOrNotDelivered(hr, "get_pattern", ValueLabel), false);
        }

        bool delivered;
        try
        {
            var setHr = Calls.CallWithBstr(pattern, Slots.ValuePatternSetValue, value);
            delivered = setHr >= 0 || Chain.ClassifyWriteOrNotDelivered(setHr, "SetValue", ValueLabel);
        }
        finally
        {
            Calls.Release(pattern);
        }

        return !delivered ? DeliveryOutcome.NotDelivered : Chain.FromObservation(GatedPatternValueEquals(element, value));
    }

    private static DeliveryOutcome InvokeRangeSet(Element element, double target)
    {
        var hr = element.CurrentPattern(Slots.PatternRangeValue, out var pattern);
        if (hr < 0 || pattern == IntPtr.Zero)
        {
            Calls.Release(pattern);
            return Chain.FromDelivery(Chain.ClassifyWriteOrNotDelivered(hr, "get_pattern", RangeLabel), false);
        }

        try
        {
            var roHr = Calls.CallIntOut(pattern, Slots.RangeValuePatternGetCurrentIsReadOnly, out var isReadOnly);
            if (roHr < 0)
            {
                return Chain.FromDelivery(Chain.ClassifyWriteOrNotDelivered(roHr, "IsReadOnly", RangeLabel), false);
            }

            if (isReadOnly != 0)
            {
                return DeliveryOutcome.NotDelivered;
            }

            var setHr = Calls.CallWithDouble(pattern, Slots.RangeValuePatternSetValue, target);
            var delivered = setHr >= 0 || Chain.ClassifyWriteOrNotDelivered(setHr, "SetValue", RangeLabel);
            if (!delivered)
            {
                return DeliveryOutcome.NotDelivered;
            }

            if (NodeProperties.IsLivePassword(element))
            {
                return DeliveryOutcome.DeliveredUnobserved;
            }

            var valHr = Calls.CallDoubleOut(pattern, Slots.RangeValuePatternGetCurrentValue, out var observed);
            if (valHr < 0)
            {
                throw ReadFailed("RangeValuePattern.get_value", valHr);
            }

            return Chain.FromObservation(Math.Abs(observed - target) < 1e-9);
        }
        finally
        {
            Calls.Release(pattern);
        }
    }

    private static bool ValueWritable(Element element) =>
        element.CurrentProperty(PropertyIds.IsValuePatternAvailable).Value is true
        && element.CurrentProperty(PropertyIds.ValueIsReadOnly).Value is false;

    private static bool RangeAvailable(Element element) =>
        element.CurrentProperty(PropertyIds.IsRangeValuePatternAvailable).Value is true;

    private static AdapterError ReadFailed(string api, int hresult) =>
        new AdapterError(ErrorCode.ActionFailed, $"{api} could not re-read the control value after delivery")
            .WithPlatformDetail(UiaFailure.Detail(hresult))
            .WithDisposition(DeliverySemantics.DeliveredUnverified());

    private static AdapterError AttachValueChars(AdapterError error, string value) =>
        error.WithDetails(new JsonObject { ["value_chars"] = value.EnumerateRunes().Count() });
}
