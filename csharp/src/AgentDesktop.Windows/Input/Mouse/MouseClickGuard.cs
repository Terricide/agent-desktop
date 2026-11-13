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

    /// <summary>Posts the corrective button-up through the mouse send layer. The post is best-effort:
    /// <c>SendInput</c> acknowledges nothing, so the guard's own record of what it posted is the only
    /// account of the corrective input.</summary>
    internal static Action<uint> ReleasePost { get; set; } =
        flag => MouseSend.PostMouseInputs([MouseSend.ButtonInput(flag)]);

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
