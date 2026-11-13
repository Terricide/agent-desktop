using AgentDesktop.Core.Signal;
using AgentDesktop.Core.Snapshot;

namespace AgentDesktop.Windows.Signal;

/// <summary>Builds the two surface signal kinds the baseline can emit. These are the only constructors, so a new kind must be added here and to the advertised set together.</summary>
internal static class SignalSurfaceFactory
{
    internal static SurfaceSignal Menu(SignalSurfaceTarget target) =>
        new(SnapshotSurface.Menu, target.Name, target.Pid, target.Instance, $"menu-{target.Pid.Value}", null);

    internal static SurfaceSignal Sheet(SignalSurfaceTarget target, string windowId) =>
        new(SnapshotSurface.Sheet, target.Name, target.Pid, target.Instance, windowId, null);
}
