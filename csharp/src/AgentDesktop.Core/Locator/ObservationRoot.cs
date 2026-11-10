using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Core.Locator;

/// <summary>Where an observation starts. Port of the Rust ObservationRoot enum
/// (crates/core/src/locator/live_locator/observation_root.rs): an application, a window, or a
/// native element a stored ref names.</summary>
public abstract record ObservationRoot
{
    /// <summary>An application root, with the surface kind it roots.</summary>
    public sealed record Application(AppInfo App, SnapshotSurface SurfaceKind) : ObservationRoot;

    /// <summary>A window root. Its surface is the window itself.</summary>
    public sealed record Window(WindowInfo Info) : ObservationRoot;

    /// <summary>An element root: a native handle, the ref entry it was captured from, and the
    /// ref text the caller passed.</summary>
    public sealed record Element(Adapter.NativeHandle Handle, RefEntry Entry, string? RootRef) : ObservationRoot;

    /// <summary>The surface this root presents: the surface an application root named, the
    /// window for a window root, and the surface the stored ref was captured from for an element
    /// root.</summary>
    public SnapshotSurface Surface() => this switch
    {
        Application application => application.SurfaceKind,
        Window => SnapshotSurface.Window,
        Element element => element.Entry.Source.Surface,
        _ => throw new ArgumentOutOfRangeException(nameof(Surface), "the root is not a known kind"),
    };
}
