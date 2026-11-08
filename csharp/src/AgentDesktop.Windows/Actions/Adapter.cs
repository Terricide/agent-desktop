using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Windows.Actions;

/// <summary>The Windows action sub-adapter: executes UI Automation pattern actions on a
/// strictly resolved element and classifies every write outcome with its delivery
/// disposition. Mirrors ActionsAdapter in crates/windows/src/actions/mod.rs.</summary>
internal sealed class ActionsAdapter : ActionOps
{
    public ActionResult ExecuteAction(NativeHandle handle, ActionRequest request, InteractionLease lease)
    {
        var deadline = lease.Deadline();
        if (handle.IsNull())
        {
            throw InvalidNativeHandle(true);
        }

        var element = handle.Downcast<Element>() ?? throw InvalidNativeHandle(false);
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        var policy = request.Headed ? InteractionPolicy.Physical : InteractionPolicy.Semantic;
        return request.Action.Kind switch
        {
            ActionKind.Click => ExecuteClick(element, policy, deadline),
            ActionKind.SetFocus => ToggleAndDisclosure.FocusElement(element, request.Headed, deadline),
            ActionKind.ScrollTo => ExecuteScrollTo(element, deadline),
            ActionKind.SetValue => ActionResult.FromExecution(
                request.Action,
                ValueWrite.SetValueSteps(element, request.Action.Text ?? "", policy, deadline)),
            ActionKind.Clear => ActionResult.FromExecution(
                request.Action,
                ValueWrite.ClearSteps(element, policy, deadline)),
            ActionKind.Toggle => ActionResult.FromExecution(
                request.Action,
                ToggleAndDisclosure.ToggleSteps(element, policy, deadline)),
            ActionKind.Check => ActionResult.FromExecution(
                request.Action,
                ToggleAndDisclosure.CheckSteps(element, true, deadline)),
            ActionKind.Uncheck => ActionResult.FromExecution(
                request.Action,
                ToggleAndDisclosure.CheckSteps(element, false, deadline)),
            ActionKind.Expand => ActionResult.FromExecution(
                request.Action,
                ToggleAndDisclosure.DisclosureSteps(element, true, policy, deadline)),
            ActionKind.Collapse => ActionResult.FromExecution(
                request.Action,
                ToggleAndDisclosure.DisclosureSteps(element, false, policy, deadline)),
            ActionKind.Select => ActionResult.FromExecution(
                request.Action,
                SelectAction.SelectSteps(element, request.Action.Text ?? "", deadline)),
            ActionKind.Scroll => ActionResult.FromExecution(
                request.Action,
                ScrollAction.ScrollSteps(
                    element,
                    request.Action.Direction ?? Direction.Down,
                    request.Action.Amount ?? 3,
                    deadline)),
            ActionKind.KeyDown or ActionKind.KeyUp or ActionKind.Hover or ActionKind.Drag =>
                throw AdapterLevelRejection(request.Action.Name()),
            _ => throw AdapterError.NotSupported(request.Action.Name()),
        };
    }

    public void ScrollIntoView(NativeHandle handle, InteractionLease lease)
    {
        if (handle.IsNull())
        {
            throw InvalidNativeHandle(true);
        }

        var element = handle.Downcast<Element>() ?? throw InvalidNativeHandle(false);
        ScrollAction.ScrollIntoViewOutcome(element, lease.Deadline());
    }

    internal static List<ActionStep> ClickChainJudgedFor(
        Deadline deadline,
        InteractionPolicy policy,
        bool invokeAvailable,
        bool legacyAvailable,
        Func<DeliveryOutcome> invoke,
        Func<DeliveryOutcome> legacy) =>
        Chain.Execute(
            deadline,
            Chain.ClickChain,
            policy,
            new[]
            {
                new ChainRung(Chain.InvokeLabel, false, Chain.Gated(invokeAvailable, invoke)),
                new ChainRung(Chain.LegacyLabel, false, Chain.Gated(legacyAvailable, legacy)),
            });

    private static ActionResult ExecuteClick(Element element, InteractionPolicy policy, Deadline deadline)
    {
        var steps = ClickChainJudgedFor(
            deadline,
            policy,
            Chain.InvokeAvailable(element),
            Chain.LegacyAvailable(element),
            () => Chain.FromDelivery(Chain.InvokePatternDelivered(element), false),
            () => Chain.FromDelivery(Chain.LegacyDefaultActionDelivered(element), false));
        return ActionResult.FromExecution(new Action { Kind = ActionKind.Click }, steps);
    }

    private static ActionResult ExecuteScrollTo(Element element, Deadline deadline)
    {
        var (label, outcome) = ScrollAction.ScrollIntoViewOutcome(element, deadline);
        return ActionResult.FromExecution(
            new Action { Kind = ActionKind.ScrollTo },
            new List<ActionStep> { Chain.BuildStep(label, outcome) });
    }

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

