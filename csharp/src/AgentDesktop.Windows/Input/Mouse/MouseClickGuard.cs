using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Mouse;

/// <summary>Click delivery and release-guard state. The guard arms immediately before each
/// mouse-down and disarms only after the matching up posts. Disposal is the sole place that
/// posts a corrective up when the sequence aborts mid-click. Cleanup is best-effort like every
/// other post here: `SendInput`'s return value is never treated as delivery evidence (A9-3).</summary>
internal sealed class MouseClickGuard : IDisposable
{
    internal const string ClickSuggestion =
        "Inspect whether a mouse button may still be held before retrying; the emergency release was posted without an OS acknowledgement";

    private readonly uint _upFlag;

    private readonly ReleaseState _state = new();

    private bool _disposed;

    internal MouseClickGuard(uint upFlag) => _upFlag = upFlag;

    /// <summary>The one seam that posts a button-up. Production posts nothing: the mouse send
    /// layer is not ported, so the first post refuses. A test swaps the sink to record the posts.</summary>
    internal static Action<uint> ReleasePost { get; set; } =
        _ => throw AdapterError.NotSupported("mouse click");

    internal void Arm() => _state.Arm();

    internal void Disarm() => _state.Disarm();

    internal bool ShouldRelease() => _state.ShouldRelease();

    /// <summary>Posts the corrective up at most once. The Rust `Drop` runs exactly once, so the
    /// guard records the first disposal and makes every later call a no-op.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!ShouldRelease())
        {
            return;
        }

        ReleasePost(_upFlag);
    }
}
