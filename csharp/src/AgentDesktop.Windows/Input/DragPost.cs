using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Input.Mouse;
using System.Collections.Generic;

namespace AgentDesktop.Windows.Input;

/// <summary>One mouse event the drag sequence posts: an absolute move to a screen point, or a
/// left-button edge. Carries the flag set the mouse send layer consumes.</summary>
internal readonly record struct DragEvent(Point? Point, bool ButtonDown, bool ButtonUp);

/// <summary>The one seam that posts drag mouse events. Production posts through the mouse send
/// layer; a test swaps the sink to record the sequence the Rust fake sink records.</summary>
internal static class DragPost
{
    internal static Action<DragEvent> Sink = PostThroughMouseSend;

    internal static void Post(DragEvent drag) => Sink(drag);

    /// <summary>One drag event as the mouse inputs it names: an absolute move to the normalized
    /// point, then the button edge. The move carries the virtual-desktop flag the point's own
    /// rect chose, so a drag that leaves the primary monitor stays in the space it lives in.</summary>
    private static void PostThroughMouseSend(DragEvent drag)
    {
        var inputs = new List<MouseInputEvent>();
        if (drag.Point is { } point)
        {
            var normalized = MouseCoord.NormalizePoint(point);
            inputs.Add(MouseSend.MoveInput(normalized.X, normalized.Y, normalized.VirtualDesktop));
        }

        if (drag.ButtonDown)
        {
            inputs.Add(MouseSend.ButtonInput(MouseSend.MouseEventfLeftDown));
        }

        if (drag.ButtonUp)
        {
            inputs.Add(MouseSend.ButtonInput(MouseSend.MouseEventfLeftUp));
        }

        MouseSend.PostMouseInputs(inputs);
    }
}
