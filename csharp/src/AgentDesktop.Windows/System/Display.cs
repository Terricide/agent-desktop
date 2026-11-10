using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Display enumeration and selection helpers for screenshot targeting.
/// </summary>
internal static class Display
{
    private const uint MonitorPrimaryFlag = 1;
    private const int MdtEffectiveDpi = 0;
    private const double DefaultDpi = 96.0;

    /// <summary>
    /// Enumerates the active monitors and reports each with bounds, the primary
    /// flag, and <c>Scale</c> derived from effective DPI: the applied value, not
    /// the requested one (A10-3's carried warning: a successful scale request is
    /// not evidence the scale applied).
    /// </summary>
    /// <remarks>
    /// The dev box and both CI environments have exactly one 96-DPI display
    /// (A10-3), so the per-monitor code lands with single-monitor evidence; the
    /// multi-monitor path is unverified until it runs on a rig with more than one
    /// monitor.
    /// </remarks>
    internal static List<DisplayInfo> ListDisplaysLive(Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (!OperatingSystem.IsWindows())
        {
            return new List<DisplayInfo>();
        }

        var state = new EnumerationState();
        MonitorEnumProc callback = (monitor, hdc, rect, lparam) => CollectMonitor(monitor, state);
        int enumerated = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        switch (ClassifyEnumeration(enumerated != 0, state.InfoReadFailed, state.DpiReadFailed))
        {
            case EnumerationOutcome.Completed:
                break;
            case EnumerationOutcome.MonitorInfoUnreadable:
                throw AdapterError.Internal("Could not read a monitor's bounds and primary flag");
            case EnumerationOutcome.DpiUnreadable:
                throw AdapterError.Internal("Could not read a monitor's effective DPI");
            case EnumerationOutcome.EnumerationFailed:
                throw EnumerationFailed();
            default:
                throw AdapterError.Internal("Unknown display enumeration outcome");
        }

        PrimariesFirst(state.Displays);
        return state.Displays;
    }

    /// <summary>
    /// What an enumeration pass actually established, kept separate from the
    /// Win32 call so the three outcomes can be told apart in a test on any host.
    /// </summary>
    internal enum EnumerationOutcome
    {
        Completed,
        MonitorInfoUnreadable,
        DpiUnreadable,
        EnumerationFailed,
    }

    /// <summary>
    /// <c>EnumDisplayMonitors</c> returning zero is a failure, not an empty desktop.
    /// Reading it as an empty success made every caller report "no displays
    /// attached" for what was an API failure, the one answer a caller cannot
    /// recover from because it looks like a fact about the machine.
    /// </summary>
    /// <remarks>
    /// A monitor whose info cannot be read is the same collapse one monitor down:
    /// continuing the enumeration dropped that monitor from the list silently, so a
    /// desktop whose every monitor failed answered the same empty success a
    /// display-less machine answers. Either unreadable read stops the pass and is
    /// reported as itself. The callback returns zero on the first failing read, so
    /// at most one of the two flags is ever set; the info read runs first, so it is
    /// checked first.
    /// </remarks>
    internal static EnumerationOutcome ClassifyEnumeration(
        bool enumerated,
        bool infoReadFailed,
        bool dpiReadFailed)
    {
        if (infoReadFailed)
        {
            return EnumerationOutcome.MonitorInfoUnreadable;
        }

        if (dpiReadFailed)
        {
            return EnumerationOutcome.DpiUnreadable;
        }

        if (!enumerated)
        {
            return EnumerationOutcome.EnumerationFailed;
        }

        return EnumerationOutcome.Completed;
    }

    internal static DisplayInfo DisplayAt(int index, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var displays = ListDisplaysLive(deadline);
        if (index < 0 || index >= displays.Count)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "display index out of range")
                .WithSuggestion("Run 'list-displays' to refresh display indexes, then retry.");
        }

        return displays[index];
    }

    internal static (int Index, DisplayInfo Live) CaptureSelection(
        DisplayInfo expected,
        Deadline deadline)
    {
        deadline.EnsureRemaining();
        var displays = ListDisplaysLive(deadline);
        var (index, live) = FindDisplayById(displays, expected.Id);
        VerifyDisplayIdentity(index, expected, live);
        return (index, live);
    }

    internal static double ScaleForBounds(Rect? bounds, Deadline deadline)
    {
        return DisplayForBounds(bounds, deadline).Scale;
    }

    internal static DisplayInfo DisplayForBounds(Rect? bounds, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var displays = ListDisplaysLive(deadline);
        return SelectDisplay(displays, bounds);
    }

    /// <summary>
    /// Corroborates display identity before and after a capture. The id is a
    /// recyclable <c>monitor-{HMONITOR}</c> handle, so bounds, primary, and scale
    /// are checked together with it rather than trusting the id alone.
    /// </summary>
    internal static void VerifyDisplayIdentity(int index, DisplayInfo expected, DisplayInfo current)
    {
        if (DisplayIdentityMatches(expected, current))
        {
            return;
        }

        throw new AdapterError(
                ErrorCode.InvalidArgs,
                $"Display at index {index} changed from '{expected.Id}' to '{current.Id}'")
            .WithSuggestion("Run 'list-displays' to refresh display indexes, then retry.");
    }

    internal static bool DisplayIdentityMatches(DisplayInfo expected, DisplayInfo current)
    {
        return expected.Id == current.Id
            && SameRect(expected.Bounds, current.Bounds)
            && expected.IsPrimary == current.IsPrimary
            && expected.Scale == current.Scale;
    }

    /// <summary>
    /// Turns a raw <c>GetDpiForMonitor</c> result into a scale, or <c>null</c> when
    /// the read cannot be trusted.
    /// </summary>
    /// <remarks>
    /// <paramref name="effective"/> is the call's own success code (<c>S_OK</c> is
    /// zero); a non-zero <paramref name="dpiX"/> on a failed call is leftover data,
    /// not evidence. Collapsing a failed read to 1.0 would be a definite claim from
    /// no evidence, the shape the Evidence Tri-State rule (<c>CONCEPTS.md</c>)
    /// forbids; the caller propagates <c>null</c> as a read failure instead of
    /// guessing.
    /// </remarks>
    internal static double? EffectiveDpiScale(int effective, uint dpiX)
    {
        if (effective == 0 && dpiX > 0)
        {
            return dpiX / DefaultDpi;
        }

        return null;
    }

    /// <summary>
    /// Orders the display list with the primary first, mirroring macOS's
    /// primary-first ordering. The sort is stable and then ordered by id.
    /// </summary>
    internal static void PrimariesFirst(List<DisplayInfo> displays)
    {
        var ordered = displays
            .OrderByDescending(display => display.IsPrimary)
            .ThenBy(display => display.Id, StringComparer.Ordinal)
            .ToList();
        displays.Clear();
        displays.AddRange(ordered);
    }

    internal static double ScaleForBoundsIn(IReadOnlyList<DisplayInfo> displays, Rect? bounds)
    {
        return SelectDisplay(displays, bounds).Scale;
    }

    internal static (int Index, DisplayInfo Live) CaptureSelectionIn(
        IReadOnlyList<DisplayInfo> displays,
        DisplayInfo expected)
    {
        var (index, live) = FindDisplayById(displays, expected.Id);
        VerifyDisplayIdentity(index, expected, live);
        return (index, live);
    }

    internal static double IntersectionArea(Rect left, Rect right)
    {
        double width = Math.Min(left.X + left.Width, right.X + right.Width) - Math.Max(left.X, right.X);
        double height = Math.Min(left.Y + left.Height, right.Y + right.Height) - Math.Max(left.Y, right.Y);
        return Math.Max(width, 0.0) * Math.Max(height, 0.0);
    }

    private static (int Index, DisplayInfo Live) FindDisplayById(
        IReadOnlyList<DisplayInfo> displays,
        string id)
    {
        for (int index = 0; index < displays.Count; index++)
        {
            if (displays[index].Id == id)
            {
                return (index, displays[index]);
            }
        }

        throw MissingDisplayError(id);
    }

    private static AdapterError MissingDisplayError(string id)
    {
        return new AdapterError(ErrorCode.InvalidArgs, $"Display '{id}' is no longer active")
            .WithSuggestion("Run 'list-displays' to refresh display indexes, then retry.");
    }

    private static DisplayInfo SelectDisplay(IReadOnlyList<DisplayInfo> displays, Rect? bounds)
    {
        DisplayInfo? selected = null;
        if (bounds is Rect requested)
        {
            selected = BestOverlap(displays, requested);
        }

        selected ??= displays.FirstOrDefault(display => display.IsPrimary);
        selected ??= displays.FirstOrDefault();
        return selected
            ?? throw new AdapterError(ErrorCode.InvalidArgs, "no displays enumerated")
                .WithSuggestion("Retry after a display is attached, or capture an ExactWindow target instead.");
    }

    /// <summary>
    /// Returns the display with the largest overlap with <paramref name="requested"/>,
    /// choosing the later display on ties (as Rust's <c>max_by</c> does), or
    /// <c>null</c> when no display overlaps.
    /// </summary>
    private static DisplayInfo? BestOverlap(IReadOnlyList<DisplayInfo> displays, Rect requested)
    {
        DisplayInfo? best = null;
        double bestArea = 0.0;
        foreach (var display in displays)
        {
            double area = IntersectionArea(requested, display.Bounds);
            if (best is null || area.CompareTo(bestArea) >= 0)
            {
                best = display;
                bestArea = area;
            }
        }

        if (best is not null && IntersectionArea(requested, best.Bounds) > 0.0)
        {
            return best;
        }

        return null;
    }

    private static bool SameRect(Rect left, Rect right)
    {
        return left.X == right.X
            && left.Y == right.Y
            && left.Width == right.Width
            && left.Height == right.Height;
    }

    private static int CollectMonitor(IntPtr monitor, EnumerationState state)
    {
        var info = new NativeMonitorInfo { CbSize = (uint)Marshal.SizeOf<NativeMonitorInfo>() };
        if (GetMonitorInfoW(monitor, ref info) == 0)
        {
            state.InfoReadFailed = true;
            return 0;
        }

        bool primary = (info.DwFlags & MonitorPrimaryFlag) != 0;
        int effective = GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _);
        double? scale = EffectiveDpiScale(effective, dpiX);
        if (scale is not double applied)
        {
            state.DpiReadFailed = true;
            return 0;
        }

        state.Displays.Add(new DisplayInfo(
            $"monitor-{monitor.ToInt64()}",
            ToRect(info.RcMonitor),
            primary,
            applied));
        return 1;
    }

    private static Rect ToRect(NativeRect native)
    {
        return new Rect
        {
            X = native.Left,
            Y = native.Top,
            Width = native.Right - native.Left,
            Height = native.Bottom - native.Top,
        };
    }

    /// <summary>
    /// An enumeration that fails returns zero, which is not an empty desktop.
    /// Reporting it as an empty success made every caller say "no displays
    /// attached" for what was an API failure.
    /// </summary>
    private static AdapterError EnumerationFailed()
    {
        int code = Marshal.GetLastPInvokeError();
        return AdapterError.Internal("The attached displays could not be enumerated")
            .WithPlatformDetail($"EnumDisplayMonitors Win32 error {code}")
            .WithSuggestion("Retry once the session has an interactive desktop attached");
    }

    private sealed class EnumerationState
    {
        public List<DisplayInfo> Displays { get; } = new();

        public bool InfoReadFailed { get; set; }

        public bool DpiReadFailed { get; set; }
    }

    private delegate int MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr lparam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMonitorInfo
    {
        public uint CbSize;
        public NativeRect RcMonitor;
        public NativeRect RcWork;
        public uint DwFlags;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr clip,
        MonitorEnumProc callback,
        IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMonitorInfoW(IntPtr monitor, ref NativeMonitorInfo info);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
