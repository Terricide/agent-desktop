using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.Tree;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Windows.Actions;

/// <summary>
/// Routes every <see cref="ActionKind"/> to its element-level implementation. Click routes through
/// the Invoke and Legacy chain. SetValue and Clear route through <see cref="ValueWrite"/>. Toggle,
/// Check, Uncheck, Expand and Collapse route through <see cref="ToggleAndDisclosure"/>. Select and
/// Scroll route through their dedicated modules. SetFocus routes through
/// <see cref="ToggleAndDisclosure.FocusElement"/>. ScrollTo runs the ScrollIntoView spine. Physical
/// keyboard legs route through <see cref="KeyboardLeg"/> and physical pointer legs through
/// <see cref="PhysicalClick"/>. Mirrors execute_action_impl in
/// crates/windows/src/actions/dispatch.rs.
/// </summary>
internal static class Dispatch
{
    /// <summary>Availability flags for the Click chain's two rungs, bundled so
    /// <see cref="ClickChainJudgedFor"/> stays within the five-parameter cap.</summary>
    internal sealed record ClickAvailability(bool InvokeAvailable, bool LegacyAvailable);

    internal static ActionResult ExecuteAction(NativeHandle handle, ActionRequest request, InteractionLease lease)
    {
        var deadline = lease.Deadline();
        if (handle.IsNull())
        {
            return NullHandleAction(request.Action, deadline);
        }

        var element = handle.Downcast<Element>() ?? throw InvalidNativeHandle(false);
        deadline.EnsureRemaining();
        LiveRead.CorroborateVerifiedProcess(element);

        var action = request.Action;
        var policy = request.Headed ? InteractionPolicy.Physical : InteractionPolicy.Semantic;
        return action.Kind switch
        {
            ActionKind.Click => ExecuteClick(action, element, policy, deadline),
            ActionKind.SetFocus => ToggleAndDisclosure.FocusElement(element, request.Headed, deadline),
            ActionKind.ScrollTo => ExecuteScrollTo(action, element, deadline),
            ActionKind.TypeText => ActionResult.FromExecution(
                action,
                KeyboardLeg.TypeTextSteps(element, RequiredText(action), policy, deadline)),
            ActionKind.PressKey => ActionResult.FromExecution(
                action,
                KeyboardLeg.PressKeySteps(element, RequiredKey(action), policy, deadline)),
            ActionKind.DoubleClick => ActionResult.FromExecution(
                action,
                PhysicalClick.DoubleClickSteps(element, policy, deadline, VerifiedPoint)),
            ActionKind.TripleClick => ActionResult.FromExecution(
                action,
                PhysicalClick.TripleClickSteps(element, policy, deadline, VerifiedPoint)),
            ActionKind.RightClick => ActionResult.FromExecution(
                action,
                PhysicalClick.RightClickSteps(element, policy, deadline, VerifiedPoint)),
            ActionKind.KeyDown or ActionKind.KeyUp or ActionKind.Hover or ActionKind.Drag =>
                throw AdapterLevelRejection(action.Name()),
            ActionKind.SetValue => ActionResult.FromExecution(
                action,
                ValueWrite.SetValueSteps(element, RequiredText(action), policy, deadline)),
            ActionKind.Clear => ActionResult.FromExecution(
                action,
                ValueWrite.ClearSteps(element, policy, deadline)),
            ActionKind.Toggle => ActionResult.FromExecution(
                action,
                ToggleAndDisclosure.ToggleSteps(element, policy, deadline)),
            ActionKind.Check => ActionResult.FromExecution(
                action,
                ToggleAndDisclosure.CheckSteps(element, true, deadline)),
            ActionKind.Uncheck => ActionResult.FromExecution(
                action,
                ToggleAndDisclosure.CheckSteps(element, false, deadline)),
            ActionKind.Expand => ActionResult.FromExecution(
                action,
                ToggleAndDisclosure.DisclosureSteps(element, true, policy, deadline)),
            ActionKind.Collapse => ActionResult.FromExecution(
                action,
                ToggleAndDisclosure.DisclosureSteps(element, false, policy, deadline)),
            ActionKind.Select => ActionResult.FromExecution(
                action,
                SelectAction.SelectSteps(element, RequiredText(action), deadline)),
            ActionKind.Scroll => ActionResult.FromExecution(
                action,
                ScrollAction.ScrollSteps(
                    element,
                    action.Direction ?? Direction.Down,
                    action.Amount ?? 3,
                    deadline)),
            _ => throw AdapterError.NotSupported(action.Name()),
        };
    }

    /// <summary>Click chain with injected rungs, the unit-test seam and the live path.</summary>
    internal static List<ActionStep> ClickChainJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        ClickAvailability availability,
        Func<DeliveryOutcome> invoke,
        Func<DeliveryOutcome> legacy) =>
        Chain.ExecuteChain(
            deadline,
            Chain.ClickChain,
            policy,
            new[]
            {
                new ChainRung(Chain.InvokeLabel, false, Chain.Gated(availability.InvokeAvailable, invoke)),
                new ChainRung(Chain.LegacyLabel, false, Chain.Gated(availability.LegacyAvailable, legacy)),
            });

    /// <summary>A null handle is the global key target: a chord pressed to whatever window is
    /// frontmost, with no element to gate on. Every other action needs an element.</summary>
    private static ActionResult NullHandleAction(Action action, Deadline deadline)
    {
        if (action.Kind != ActionKind.PressKey)
        {
            throw InvalidNativeHandle(true);
        }

        KeyboardEvent.SynthesizeKey(RequiredKey(action), deadline);
        return ActionResult.FromExecution(action, [new ActionStep("PressKey", "succeeded", "physical_synthetic")]);
    }

    private static ActionResult ExecuteClick(Action action, Element element, InteractionPolicy policy, Deadline deadline)
    {
        var steps = ClickChainJudgedFor(
            deadline,
            policy,
            new ClickAvailability(Chain.InvokeAvailable(element), Chain.LegacyAvailable(element)),
            () => Chain.FromDelivery(Chain.InvokePatternDelivered(element), false),
            () => Chain.FromDelivery(Chain.LegacyDefaultActionDelivered(element), false));
        return ActionResult.FromExecution(action, steps);
    }

    private static ActionResult ExecuteScrollTo(Action action, Element element, Deadline deadline)
    {
        var (label, outcome) = ScrollAction.ScrollIntoViewOutcome(element, deadline);
        return ActionResult.FromExecution(action, new List<ActionStep> { Chain.BuildStep(label, outcome) });
    }

    /// <summary>SEAM: the ref's recorded pointer target. The C# ActionRequest has no verified point
    /// field yet, so every request is treated as carrying none, which the physical legs read as
    /// the live bounds midpoint, the same as the Rust path with no verified point.</summary>
    private static Point? VerifiedPoint => null;

    private static string RequiredText(Action action) =>
        action.Text ?? throw new AdapterError(ErrorCode.InvalidArgs, $"{action.Name()} requires text");

    private static KeyCombo RequiredKey(Action action) =>
        action.Key ?? throw new AdapterError(ErrorCode.InvalidArgs, $"{action.Name()} requires a key combination");

    private static AdapterError InvalidNativeHandle(bool empty) =>
        new AdapterError(ErrorCode.InvalidArgs, "Native handle does not contain a Windows UI Automation element")
            .WithDetails(new JsonObject
            {
                ["kind"] = "invalid_native_handle",
                ["platform"] = "windows",
                ["empty"] = empty,
            });

    private static AdapterError AdapterLevelRejection(string label) =>
        new AdapterError(ErrorCode.ActionNotSupported, $"{label} requires adapter-level handling, not element action")
            .WithSuggestion("Use the top-level command (e.g. 'hover', 'drag', 'press') instead of targeting an element.");
}
