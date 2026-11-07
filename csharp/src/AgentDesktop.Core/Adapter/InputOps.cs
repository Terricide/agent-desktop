using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Adapter;

/// <summary>Synthesizing raw keyboard, mouse, and clipboard input. Every method defaults to a refusal.</summary>
public interface InputOps
{
    /// <summary>The reason physical pointer delivery cannot happen in this session, if it cannot. Commands that would aim a cursor ask before any geometry work, so a session without pointer input answers at once instead of spending its wait budget on a gesture it can never send.</summary>
    AdapterError? PhysicalPointerRefusal() => null;

    /// <summary>Whether a headed keyboard delivery needs the target window activated first. A compositor that refuses client activation answers false, so core skips a focus the session cannot grant.</summary>
    bool KeyboardNeedsWindowActivation() => true;

    OperationReport MouseEvent(MouseEvent mouseEvent, InteractionLease lease) =>
        throw AdapterError.NotSupported("mouse_event");

    void KeyEvent(KeyCombo combo, bool down, InteractionLease lease) =>
        throw AdapterError.NotSupported("key_event");

    OperationReport Drag(DragParams parameters, InteractionLease lease) =>
        throw AdapterError.NotSupported("drag");

    OperationReport ClearClipboard(InteractionLease lease) =>
        throw AdapterError.NotSupported("clear_clipboard");

    /// <summary>Reads the requested clipboard representation. Null means the pasteboard has no data of that shape, a normal outcome distinct from the refusal that means this platform never implements clipboard reads.</summary>
    ClipboardContent? GetClipboardContent(ClipboardFormat format, Deadline deadline) =>
        throw AdapterError.NotSupported("get_clipboard_content");

    OperationReport SetClipboardContent(ClipboardContent content, InteractionLease lease) =>
        throw AdapterError.NotSupported("set_clipboard_content");
}
