using System.Collections.Generic;
using System.Runtime.InteropServices;
using AgentDesktop.Windows.System.Runtime;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The live monitor list and the refresh rate, read from the desktop.
/// The refresh source is <c>GetDeviceCaps(VREFRESH)</c> on the screen DC, not <c>EnumDisplaySettings</c>: the
/// obvious call fails on this class of host and leaves its frequency at zero, which a frame clock would take as
/// an infinite rate. The clamp downstream is the second guard; this is the first.
/// The monitor record is the overlay's own <c>OverlayMonitor</c> rather than core's <c>DisplayInfo</c> because the
/// overlay needs each monitor's work area to place a label and to pick a resting point, and <c>DisplayInfo</c>
/// carries none. Extending it would change <c>list-displays</c> output on both platforms.
/// </summary>
internal static class DisplayProbe
{
    private const double UserDefaultScreenDpi = 96.0;
    private const int VRefresh = 116;
    private const int MdtEffectiveDpi = 0;
    private const uint MonitorInfoFPrimary = 1;

    /// <summary>
    /// The screen's vertical refresh rate in hertz, or zero when the device reports none.
    /// </summary>
    internal static uint RefreshHz()
    {
        var screen = GetDC(global::System.IntPtr.Zero);
        var hz = GetDeviceCaps(screen, VRefresh);
        ReleaseDC(global::System.IntPtr.Zero, screen);
        return (uint)global::System.Math.Max(0, hz);
    }

    /// <summary>
    /// The monitors this desktop presents, or an empty list.
    /// An enumeration that fails surfaces here as the empty list, not as a distinct value: the caller treats an
    /// empty list as "keep the topology you already had" rather than as "no monitors are attached", so the overlay
    /// never repositions itself off a failed read. The failure is traced so a machine that returns empty for a real
    /// reason can be told apart in a log from one whose call simply failed.
    /// A monitor whose info cannot be read stops the walk rather than being skipped, so "empty" covers that case too
    /// - a shorter list is the same defect as a wrong one, because the monitor that dropped out is exactly the one a
    /// cursor standing on it would need.
    /// </summary>
    internal static List<OverlayMonitor> Monitors()
    {
        var collected = new List<OverlayMonitor>();
        MonitorEnumProc collect =
            (global::System.IntPtr monitor, global::System.IntPtr dc, ref NativeRect rect, global::System.IntPtr payload) =>
                CollectMonitor(monitor, collected);
        var enumerated = EnumDisplayMonitors(
            global::System.IntPtr.Zero,
            global::System.IntPtr.Zero,
            collect,
            global::System.IntPtr.Zero);
        var result = Completed(enumerated != 0, collected);
        global::System.GC.KeepAlive(collect);
        return result;
    }

    /// <summary>
    /// A partial monitor list is discarded rather than returned.
    /// The callback stops the walk when a monitor's info cannot be read, so a failed enumeration can leave real
    /// monitors already collected. Handing those back would place the overlay against a desktop missing whichever
    /// monitor failed - a cursor on it would map to the fallback screen and draw somewhere else entirely, silently.
    /// An empty list is the caller's signal to keep the topology it already had, which is the only reading that is
    /// true of a walk that did not finish.
    /// </summary>
    internal static List<OverlayMonitor> Completed(bool enumerated, List<OverlayMonitor> collected)
    {
        if (enumerated)
        {
            return collected;
        }
        global::System.Diagnostics.Debug.WriteLine(
            $"discarded={collected.Count}: the overlay's monitor enumeration did not complete; the caller keeps its previous topology");
        return new List<OverlayMonitor>();
    }

    private static bool CollectMonitor(global::System.IntPtr monitor, List<OverlayMonitor> collected)
    {
        var info = new MonitorInfo { CbSize = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfoW(monitor, ref info) == 0)
        {
            return false;
        }
        collected.Add(new OverlayMonitor(
            global::AgentDesktop.Windows.System.Runtime.WinRect.RectOf(info.RcMonitor),
            global::AgentDesktop.Windows.System.Runtime.WinRect.RectOf(info.RcWork),
            ScaleOf(monitor),
            (info.DwFlags & MonitorInfoFPrimary) != 0));
        return true;
    }

    private static double ScaleOf(global::System.IntPtr monitor)
    {
        var hr = GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _);
        return hr == 0 && dpiX > 0 ? dpiX / UserDefaultScreenDpi : 1.0;
    }

    private delegate bool MonitorEnumProc(
        global::System.IntPtr monitor,
        global::System.IntPtr dc,
        ref NativeRect rect,
        global::System.IntPtr payload);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint CbSize;
        public NativeRect RcMonitor;
        public NativeRect RcWork;
        public uint DwFlags;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern global::System.IntPtr GetDC(global::System.IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int ReleaseDC(global::System.IntPtr window, global::System.IntPtr dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int GetDeviceCaps(global::System.IntPtr dc, int index);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int EnumDisplayMonitors(
        global::System.IntPtr dc,
        global::System.IntPtr clip,
        MonitorEnumProc callback,
        global::System.IntPtr payload);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetMonitorInfoW(global::System.IntPtr monitor, ref MonitorInfo info);

    [DllImport("Shcore.dll", ExactSpelling = true)]
    private static extern int GetDpiForMonitor(global::System.IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
