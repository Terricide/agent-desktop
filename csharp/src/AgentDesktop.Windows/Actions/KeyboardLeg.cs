using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Actions;

/// <summary>The physical keyboard leg: the policy gate, the delivery-readiness gate, and the
/// chains that type text and press a chord as real key events. Mirrors
/// type_text_physical and press_key_element_steps in
/// crates/windows/src/actions/physical_keyboard.rs.</summary>
internal static class KeyboardLeg
{
    internal const string FocusLabel = "SetFocus";

    internal const string KeyboardLabel = "KeyboardInput";

    internal static readonly ChainDef KeyboardChain = new(
        "Use --policy semantic for a UIA TextPattern-based type, or target an editable element.",
        false);

    /// <summary>A physical key event needs a focus-steal policy: the sequence takes focus to the
    /// target window, which a headless run cannot authorize.</summary>
    internal static void EnsureKeyboardPolicy(InteractionPolicy policy)
    {
        if (policy != InteractionPolicy.Physical)
        {
            throw new AdapterError(
                ErrorCode.PolicyDenied,
                "Physical keyboard input requires a policy that allows focus-steal")
                .WithSuggestion("Use --headed, or use --policy semantic for UIA TextPattern typing")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }
    }

    /// <summary>Refuses a physical keyboard target before any key event is emitted when the
    /// element cannot show that the key events landed: a target with no readable owning process
    /// cannot be checked against the UIPI integrity boundary, and a target with no readable live
    /// bounds cannot be checked for focus, so a blocked write would be reported as success.</summary>
    internal static void EnsureKeyboardDeliveryReady(Element element, Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        var pid = element.CurrentProcessId();
        if (pid is null)
        {
            throw new AdapterError(
                ErrorCode.ActionNotSupported,
                "Physical keyboard input needs a target with a readable owning process id")
                .WithDetails("{\"raw_input_emitted\":false}")
                .WithSuggestion("Target an element in a live window, or use --policy semantic for UIA TextPattern typing")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        if (element.CurrentBoundingRectangle().Kind != LocatorFieldKind.Known)
        {
            throw new AdapterError(
                ErrorCode.ActionNotSupported,
                "Physical keyboard input needs a target with live bounds that can be checked for focus")
                .WithDetails("{\"raw_input_emitted\":false}")
                .WithSuggestion("Target an element in a live window, or use --policy semantic for UIA TextPattern typing")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        Elevation.EnsureTargetIntegrityAllowsInput(pid.Value);
    }

    /// <summary>Types text as real key events: the focus-steal rung, then the key-event rung. The
    /// key-event rung verifies focus before each chunk, so a target that loses focus mid-sequence
    /// stops the sequence rather than typing into whatever window is frontmost.</summary>
    internal static List<ActionStep> TypeTextSteps(
        Element element,
        string text,
        InteractionPolicy policy,
        Deadline deadline)
    {
        EnsureKeyboardPolicy(policy);
        EnsureKeyboardDeliveryReady(element, deadline);

        var rungs = new List<ChainRung>
        {
            new(FocusLabel, true, () => Chain.FromDelivery(element.SetFocus() >= 0, false)),
            new(KeyboardLabel, true, () => KeyboardRung(element, text, deadline)),
        };

        return Chain.Execute(deadline, KeyboardChain, policy, rungs);
    }

    /// <summary>Presses one chord as real key events, past the same gates.</summary>
    internal static List<ActionStep> PressKeySteps(
        Element element,
        KeyCombo combo,
        InteractionPolicy policy,
        Deadline deadline)
    {
        EnsureKeyboardPolicy(policy);
        EnsureKeyboardDeliveryReady(element, deadline);
        KeyboardEvent.SynthesizeKey(combo, deadline);
        return [new ActionStep(KeyboardLabel, "succeeded", "physical_synthetic")];
    }

    /// <summary>The key-event rung. Focus is re-read before every chunk, so a target that loses
    /// focus mid-sequence stops the sequence rather than typing into whatever window is frontmost.
    /// The rung's own outcome is unverified: a posted Unicode event is a completed dispatch with
    /// no OS acknowledgement, and the typed text is never read back here.</summary>
    private static DeliveryOutcome KeyboardRung(Element element, string text, Deadline deadline)
    {
        KeyboardText.SynthesizeText(text, deadline, _ => element.CurrentHasKeyboardFocus().Value == true);
        return DeliveryOutcome.DeliveredUnverified;
    }
}
