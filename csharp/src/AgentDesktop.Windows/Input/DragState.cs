using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input;

/// <summary>Drag delivery and release-guard state. The guard arms immediately before the first
/// committed injection (mouse-down) and disarms only after the final release posts. Disposal is
/// the sole place that posts corrective input, and it always targets the origin, never the
/// destination. Cleanup is best-effort like every other post here, so an abort reports
/// `emergency_release_posted` without claiming the OS acknowledged it.</summary>
internal sealed class DragReleaseGuard : IDisposable
{
    internal const string Suggestion =
        "Inspect the source and destination before retrying; the emergency release was posted without an OS acknowledgement";

    private readonly Point _origin;

    private readonly ReleaseState _state = new();

    internal DragReleaseGuard(Point origin) => _origin = origin;

    internal void Arm() => _state.Arm();

    internal void Disarm() => _state.Disarm();

    internal void MarkDelivered() => _state.MarkDelivered();

    internal bool ShouldRelease() => _state.ShouldRelease();

    internal AdapterError EnrichError(AdapterError error) => _state.EnrichError(error, Suggestion);

    public void Dispose()
    {
        if (!ShouldRelease())
        {
            return;
        }

        DragPost.Post(new DragEvent(_origin, ButtonDown: false, ButtonUp: false));
        DragPost.Post(new DragEvent(null, ButtonDown: false, ButtonUp: true));
    }
}
