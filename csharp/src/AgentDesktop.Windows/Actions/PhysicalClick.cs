using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input.Mouse;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Actions;

/// <summary>Physical click legs for `DoubleClick`, `TripleClick`, and `RightClick`: headed
/// policy, bounds midpoint, window-foreground verification, elevation gate, then `SendInput`
/// mouse synthesis. Mirrors crates/windows/src/actions/physical_click.rs.</summary>
internal static class PhysicalClick
{
    internal const string ClickLabel = "SendInput.click";

    internal const string LeftButton = "left";

    internal const string RightButton = "right";

    internal const string ClickKind = "click";

    internal const string PhysicalSyntheticMechanism = "physical_synthetic";

    /// <summary>Double-clicks a live element as real mouse events, past the same gates as the
    /// other physical click legs.</summary>
    internal static List<ActionStep> DoubleClickSteps(
        Element element,
        InteractionPolicy policy,
        Deadline deadline,
        Point? verifiedPoint) =>
        PhysicalClickSteps(element, new ClickSpec(LeftButton, 2), policy, deadline, verifiedPoint);

    /// <summary>Triple-clicks a live element as real mouse events, past the same gates as the
    /// other physical click legs.</summary>
    internal static List<ActionStep> TripleClickSteps(
        Element element,
        InteractionPolicy policy,
        Deadline deadline,
        Point? verifiedPoint) =>
        PhysicalClickSteps(element, new ClickSpec(LeftButton, 3), policy, deadline, verifiedPoint);

    /// <summary>Right-clicks a live element as real mouse events, past the same gates as the
    /// other physical click legs.</summary>
    internal static List<ActionStep> RightClickSteps(
        Element element,
        InteractionPolicy policy,
        Deadline deadline,
        Point? verifiedPoint) =>
        PhysicalClickSteps(element, new ClickSpec(RightButton, 1), policy, deadline, verifiedPoint);

    /// <summary>Runs the full physical click leg: the headed policy gate, the remaining-budget
    /// gate, the delivery-readiness gate that establishes whether the target's window is
    /// foreground, and the live bounds read, then commits one click from the resulting gate.</summary>
    internal static List<ActionStep> PhysicalClickSteps(
        Element element,
        ClickSpec spec,
        InteractionPolicy policy,
        Deadline deadline,
        Point? verifiedPoint)
    {
        EnsureHeadedClickPolicy(policy);
        deadline.EnsureRemaining();
        var foregroundReady = EnsureClickDeliveryReady(element, deadline);
        var bounds = ReadClickBounds(element);
        return [ClickFromGate(new ClickGate(bounds, foregroundReady), verifiedPoint, spec, deadline)];
    }

    /// <summary>Commits one click once the gate is established: refuses before any input is
    /// posted when the target's window was not confirmed foreground, chooses the delivery point,
    /// posts the mouse events, and records the step as a completed dispatch with no postcondition
    /// read, because `SendInput` acknowledges nothing.</summary>
    internal static ActionStep ClickFromGate(
        ClickGate gate,
        Point? verifiedPoint,
        ClickSpec spec,
        Deadline deadline)
    {
        if (!gate.ForegroundReady)
        {
            throw FocusLostBeforeDelivery();
        }

        var point = DeliveryPoint(gate.Bounds, verifiedPoint);
        SynthesizeMouse(
            new MouseEvent
            {
                Kind = ClickKind,
                Point = point,
                Button = spec.Button,
                ClickCount = spec.Count,
            },
            deadline);
        return new ActionStep(ClickLabel, "succeeded", PhysicalSyntheticMechanism, false);
    }

    /// <summary>The headed-click policy gate, shared with the keyboard leg.</summary>
    private static void EnsureHeadedClickPolicy(InteractionPolicy policy) =>
        PhysicalTarget.EnsureHeadedClickPolicy(policy);

    /// <summary>The click delivery readiness gate, shared with the keyboard leg.</summary>
    private static bool EnsureClickDeliveryReady(Element element, Deadline deadline) =>
        PhysicalTarget.EnsureClickDeliveryReady(element, deadline);

    /// <summary>The target's live bounds, shared with the keyboard leg.</summary>
    private static Rect ReadClickBounds(Element element) =>
        PhysicalTarget.ReadClickBounds(element);

    /// <summary>The delivery point, shared with the keyboard leg.</summary>
    private static Point DeliveryPoint(Rect bounds, Point? verifiedPoint) =>
        PhysicalTarget.DeliveryPoint(bounds, verifiedPoint);

    /// <summary>The focus-loss refusal, shared with the keyboard leg.</summary>
    private static AdapterError FocusLostBeforeDelivery() =>
        PhysicalTarget.FocusLostBeforeDelivery();

    /// <summary>Posts the click sequence: one move to the normalized point, then the down and up
    /// edges for each click. The post is best-effort: <c>SendInput</c> acknowledges nothing, so a
    /// completed dispatch is reported as an unverified delivery, never as a verified one. A budget
    /// spent between clicks fails before the next edge is posted.</summary>
    private static void SynthesizeMouse(MouseEvent mouseEvent, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var normalized = MouseCoord.NormalizePoint(mouseEvent.Point);
        MouseSend.PostMouseInputs([MouseSend.MoveInput(normalized.X, normalized.Y, normalized.VirtualDesktop)]);

        var (downFlag, upFlag) = ButtonFlags(mouseEvent.Button);
        for (var click = 0; click < mouseEvent.ClickCount; click++)
        {
            deadline.EnsureRemaining();
            MouseSend.PostMouseInputs([MouseSend.ButtonInput(downFlag), MouseSend.ButtonInput(upFlag)]);
        }
    }

    /// <summary>The down and up flags for the named button.</summary>
    private static (uint Down, uint Up) ButtonFlags(string button) => button switch
    {
        "left" => (MouseSend.MouseEventfLeftDown, MouseSend.MouseEventfLeftUp),
        "right" => (MouseSend.MouseEventfRightDown, MouseSend.MouseEventfRightUp),
        "middle" => (MouseSend.MouseEventfMiddleDown, MouseSend.MouseEventfMiddleUp),
        _ => throw AdapterError.InvalidArgs($"unknown mouse button {button}"),
    };
}

/// <summary>What click to perform, shared by the physical click legs and the click-from-gate step.</summary>
internal sealed record ClickSpec(string Button, int Count);

/// <summary>Preflight-established delivery gate: the target's live bounds and whether its window
/// was confirmed foreground before physical input commits.</summary>
internal sealed record ClickGate(Rect Bounds, bool ForegroundReady);
