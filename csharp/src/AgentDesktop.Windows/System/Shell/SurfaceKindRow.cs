using AgentDesktop.Core.Snapshot;

namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// One row of the shell surface kind table: what the surface roots at, how it is reached, how it is raised and
/// dismissed, and whether this build exposes it at all.
/// </summary>
internal sealed record SurfaceKindRow(
    SnapshotSurface Kind,
    SurfaceFamily Family,
    SurfaceRaise Raise,
    SurfaceDismiss Dismiss,
    bool ExistsOnBuild,
    string? CapabilityHolder);
