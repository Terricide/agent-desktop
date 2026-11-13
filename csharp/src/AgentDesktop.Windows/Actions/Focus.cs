using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Windows.Actions;

/// <summary>
/// Headed SetFocus with focused-element verification. UIA SetFocus moves the desktop foreground
/// window (A3-4, A19-5), so headless dispatch fails with POLICY_DENIED. Headed dispatch invokes
/// SetFocus through the mutation classifier and verifies by comparing the automation client's
/// focused element to the target. Port of crates/windows/src/actions/focus.rs.
/// </summary>
internal static class Focus
{
    private const string SetFocusApi = "UIElement.SetFocus";

    private const string SetFocusOp = "SetFocus";

    /// <summary>
    /// Runs SetFocus on the element when the request is headed and reports the delivery with
    /// its verification. A headless request is refused before the element is touched.
    /// </summary>
    internal static ActionResult FocusElement(Element element, ActionRequest request, Deadline deadline)
    {
        if (!request.Headed)
        {
            throw HeadlessDenied();
        }

        deadline.EnsureRemaining();
        var delivered = InvokeFocus(element);
        var verified = FocusMatchesTarget(element);
        return FocusFromDelivery(request.Headed, delivered, verified);
    }

    /// <summary>
    /// Reports whether the element already holds keyboard focus, falling back to comparing the
    /// automation client's focused element to the target.
    /// </summary>
    internal static bool KeyboardFocusReady(Element element)
    {
        if (element.CurrentHasKeyboardFocus().Value == true)
        {
            return true;
        }

        return FocusMatchesTarget(element);
    }

    /// <summary>
    /// Builds the SetFocus result from a delivery and verification pair. A headless request is
    /// refused, and an undelivered SetFocus is an ACTION_FAILED with a NOT_DELIVERED disposition.
    /// </summary>
    internal static ActionResult FocusFromDelivery(bool headed, bool delivered, bool verified)
    {
        if (!headed)
        {
            throw HeadlessDenied();
        }

        if (!delivered)
        {
            throw new AdapterError(ErrorCode.ActionFailed, "SetFocus is not available on this element")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return ActionResult.FromExecution(
            new Action { Kind = ActionKind.SetFocus },
            new List<ActionStep> { new(SetFocusApi, "succeeded", Chain.SemanticMechanism, verified) });
    }

    /// <summary>
    /// The POLICY_DENIED error for a headless SetFocus, carrying the foreground effect and the
    /// measured evidence rows that establish it.
    /// </summary>
    internal static AdapterError HeadlessDenied() =>
        new AdapterError(ErrorCode.PolicyDenied, "SetFocus moves the desktop foreground window on Windows")
            .WithSuggestion("Pass --headed when keyboard focus should move the desktop foreground window")
            .WithDetails(new JsonObject
            {
                ["foreground_effect"] = true,
                ["evidence"] = new JsonArray("A3-4", "A19-5"),
            })
            .WithDisposition(DeliverySemantics.NotDelivered());

    private static bool InvokeFocus(Element element)
    {
        var hr = element.SetFocus();
        return hr >= 0
            ? Mutation.ClassifySuccess()
            : Mutation.ClassifyMutation(SetFocusOp, SetFocusApi, WriteFailure.FromHresult(hr));
    }

    /// <summary>
    /// Whether the automation client's focused element is the target. A focused-element read
    /// that fails, or returns no pointer, means the target is not verified focused.
    /// </summary>
    private static bool FocusMatchesTarget(Element element)
    {
        var hr = Client.Create(out var client);
        if (hr < 0 || client is null)
        {
            throw UiaFailure.Error(hr, "create a UI Automation client for focus verification");
        }

        using (client)
        {
            if (client.FocusedElement(out var focusedPointer) < 0 || focusedPointer == IntPtr.Zero)
            {
                return false;
            }

            using var focused = new Element(focusedPointer);
            return client.CompareElements(element.Pointer, focused.Pointer, out var same) >= 0 && same;
        }
    }
}
