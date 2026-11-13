using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Commands;

/// <summary>Changes one window's placement: minimize, maximize, restore, resize, or move. The window is
/// chosen the way every window-scoped command chooses one. The adapter returns only once the placement
/// the operation demanded has been read back, so the report says what was verified: a geometry write
/// inside its re-read tolerance, or the placement state the verb names. A window that accepts the call
/// and never settles is reported as delivered-unverified, not as done.</summary>
public sealed class WindowLifecycleCommand
{
    private readonly PlatformAdapter _adapter;

    public WindowLifecycleCommand(PlatformAdapter adapter) => _adapter = adapter;

    /// <summary>Runs one window write. A selector names the window: an id, a title substring, or the app
    /// whose windows are the candidates. With no selector the command refuses before the application is
    /// read.</summary>
    public EnvelopeResponse Run(string command, string? windowId, string? app, string? title, WindowOp op)
    {
        var (window, refusal) = WindowSelection.Pick(_adapter, command, windowId, app, title, op.Verb());
        if (refusal is not null)
        {
            return refusal;
        }

        var deadline = Deadline.Standard();
        using var lease = new InteractionLease(deadline, InteractionPolicy.Physical);
        var report = _adapter.WindowOperation(window!, op, lease);
        return Envelope.Ok(command, OperationReportJson.Data(report));
    }
}
