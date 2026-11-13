using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Signal;

/// <summary>A surface a process presents at the moment of a baseline capture.</summary>
public sealed record SurfaceSignal(
    SnapshotSurface Kind,
    string App,
    ProcessId Pid,
    string ProcessInstance,
    string Id,
    string? Title);
