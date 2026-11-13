using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Signal;

/// <summary>A point-in-time capture of windows, apps and surfaces that a signal wait diffs against.</summary>
public sealed record SignalBaseline(
    List<WindowInfo> Windows,
    List<AppInfo> Apps,
    List<SurfaceSignal> Surfaces,
    SignalCompleteness Completeness);
