using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input;

/// <summary>One mouse event the drag sequence posts: an absolute move to a screen point, or a
/// left-button edge. Carries the flag set the mouse send layer consumes.</summary>
internal readonly record struct DragEvent(Point? Point, bool ButtonDown, bool ButtonUp);

/// <summary>The one seam that posts drag mouse events. Production posts nothing: the mouse
/// send layer is not ported, so the first post refuses before any button is pressed. A test
/// swaps the sink to record the sequence the Rust fake sink records.</summary>
internal static class DragPost
{
    internal static Action<DragEvent> Sink { get; set; } =
        _ => throw AdapterError.NotSupported("drag");

    internal static void Post(DragEvent drag) => Sink(drag);
}
