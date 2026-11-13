namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The desktop the overlay draws onto: which monitors exist, and how fast they refresh.
/// </summary>
/// <remarks>
/// The two are read together and invalidated together. A resolution change, a scale change
/// or a monitor hot-plug makes both stale at once, so they are held as a pair rather than as
/// two fields a caller has to remember to refresh in step.
/// </remarks>
internal sealed class Topology
{
    private List<OverlayMonitor> monitors;
    private uint refreshHz;

    private Topology(List<OverlayMonitor> monitors, uint refreshHz)
    {
        this.monitors = monitors;
        this.refreshHz = refreshHz;
    }

    internal IReadOnlyList<OverlayMonitor> Monitors => monitors;

    internal uint RefreshHz => refreshHz;

    internal static Topology Probe()
    {
        List<OverlayMonitor> probedMonitors = DisplayProbe.Monitors();
        uint probedRefreshHz = DisplayProbe.RefreshHz();
        return new Topology(probedMonitors, probedRefreshHz);
    }

    /// <summary>
    /// Re-reads the desktop, keeping the monitors already known when the enumeration answers none.
    /// </summary>
    /// <remarks>
    /// An enumeration that fails is reported as an empty list, which is indistinguishable from a
    /// desktop with no monitors and is what the paint path falls back to a hardcoded 1920x1080
    /// screen for. Adopting that would relocate every later frame onto a desktop that does not
    /// exist, and it would survive the fault: nothing restores the real list except another probe
    /// that happens to succeed. Absence of evidence is not a monitor being unplugged, so the last
    /// list that was actually observed stands.
    /// </remarks>
    internal void Reprobe()
    {
        Adopt(Probe());
    }

    internal void Adopt(Topology probed)
    {
        refreshHz = probed.refreshHz;
        if (probed.monitors.Count > 0)
        {
            monitors = probed.monitors;
        }
    }
}
