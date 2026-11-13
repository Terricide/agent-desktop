using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.Input.Mouse;

namespace AgentDesktop.Windows;

/// <summary>
/// Standalone pointer operations: the adapter half of `mouse-move`, `mouse-down`, `mouse-up`,
/// `mouse-click` and `mouse-wheel`. A standalone edge has no composer that verifies its effect, so the
/// report is the shared core's unverified dispatch once the post returns.
///
/// The wheel moves in notches, not pixels: the delta is divided by the per-notch wheel distance so the
/// amount the caller states in pixels becomes the notch count `mouse_event` expects. A notch shorter than
/// the wheel distance rounds to one notch, which is the smallest move the OS posts.
/// </summary>
internal static class AdapterMouseOps
{
    /// <summary>Windows posts one wheel notch as WHEEL_DELTA units of wheel data.</summary>
    internal const int WheelNotchPixels = 120;

    internal static OperationReport MouseEvent(MouseEvent mouseEvent, InteractionLease lease)
    {
        OperationReport pointerReport = OperationReportBuild.PointerDispatched(mouseEvent, false);
        MouseSend.PostMouseInputs(BuildInputs(mouseEvent));
        return pointerReport;
    }

    /// <summary>
    /// The mouse events for one pointer event. Move posts as-is; down and up add the button bits for the
    /// named button; a click is the down and up pair; the wheel carries the notch count in the wheel data,
    /// and horizontal wheel data for a horizontal notch.
    /// </summary>
    private static List<MouseInputEvent> BuildInputs(MouseEvent mouseEvent)
    {
        List<MouseInputEvent> events = new();
        Point point = mouseEvent.Point;
        uint down = ButtonBits(mouseEvent.Button);

        switch (mouseEvent.Kind)
        {
            case "move":
                events.Add(MoveTo(point));
                break;
            case "down":
                events.Add(MoveTo(point));
                events.Add(new MouseInputEvent(0, 0, 0, down));
                break;
            case "up":
                events.Add(MoveTo(point));
                events.Add(new MouseInputEvent(0, 0, 0, UpBit(down)));
                break;
            case "wheel":
                events.Add(WheelEvent(mouseEvent));
                break;
            default:
                events.Add(MoveTo(point));
                events.Add(new MouseInputEvent(0, 0, 0, down));
                events.Add(new MouseInputEvent(0, 0, 0, UpBit(down)));
                break;
        }

        return events;
    }

    /// <summary>
    /// The wheel event. The wheel has no button, so the button bits are 0; the vertical notch count goes in
    /// the wheel data, and a horizontal notch adds the horizontal wheel flag and carries its count as the
    /// horizontal delta.
    /// </summary>
    private static MouseInputEvent WheelEvent(MouseEvent mouseEvent)
    {
        int vertical = Notches(mouseEvent.DeltaY);
        int horizontal = Notches(mouseEvent.DeltaX);
        uint flags = MouseSend.MouseEventfWheel;
        if (horizontal != 0) flags |= MouseSend.MouseEventfHWheel;

        return new MouseInputEvent(0, vertical, horizontal, flags);
    }

    /// <summary>Converts pixels to wheel notches, clamped away from zero so the post is never empty.</summary>
    internal static int Notches(double pixels)
    {
        int notch = (int)Math.Round(pixels / WheelNotchPixels);
        if (notch == 0 && pixels != 0) return pixels > 0 ? 1 : -1;
        return notch;
    }

    /// <summary>The wheel event for a delta. Exposed so the notch conversion can be read without posting.</summary>
    internal static MouseInputEvent WheelEventFor(MouseEvent mouseEvent) => WheelEvent(mouseEvent);

    /// <summary>The release bit for a press bit. Exposed so the pairing can be read without posting.</summary>
    internal static uint ReleaseFor(uint press) => UpBit(press);

    /// <summary>The press bit a button name selects. Exposed so the pairing can be read without posting.</summary>
    internal static uint PressFor(string button) => ButtonBits(button);

    private static MouseInputEvent MoveTo(Point point)
    {
        var normalized = MouseCoord.NormalizePoint(point);
        return MouseSend.MoveInput(normalized.X, normalized.Y, normalized.VirtualDesktop);
    }

    private static uint ButtonBits(string button) => button switch
    {
        "right" => MouseSend.MouseEventfRightDown,
        "middle" => MouseSend.MouseEventfMiddleDown,
        _ => MouseSend.MouseEventfLeftDown,
    };

    /// <summary>The release bit for a press bit: left 0x02 to 0x04, right 0x08 to 0x10, middle 0x20 to 0x40.</summary>
    private static uint UpBit(uint down) => down switch
    {
        0x02 => MouseSend.MouseEventfLeftUp,
        0x08 => MouseSend.MouseEventfRightUp,
        0x20 => MouseSend.MouseEventfMiddleUp,
        _ => MouseSend.MouseEventfLeftUp,
    };
}
