using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows;

public sealed partial class WindowsAdapter
{
    /// <summary>Declares the process multithreaded and per-monitor-v2 DPI aware, once, before any
    /// platform work. Rust calls the same bootstrap in its CLI main before dispatch.</summary>
    /// <remarks>A process that is not per-monitor-v2 aware gets virtualized monitor rectangles and a
    /// virtualized 96-DPI reading, so `list-displays` reports a scale of 1.0 and bounds divided by the
    /// real scale, and a capture taken at those bounds samples the wrong region.</remarks>
    public static void PrepareProcess() => ComRuntime.EnsureOwnedProcessMtaAndDpi();
}
