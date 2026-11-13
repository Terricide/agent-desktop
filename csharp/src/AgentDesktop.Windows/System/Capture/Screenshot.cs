using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>Screenshot orchestration: target dispatch, identity sandwich, precedence. The
/// pixel buffer is carried as a raw byte array while the shared ImageBuffer type is not
/// ported; every seam below refuses with the shared not-supported error, so the surface
/// fails closed instead of inventing capture behaviour.</summary>
internal static class Screenshot
{
    /// <summary>Runs one screenshot target. A refusal that established no delivery evidence
    /// is coerced to not_delivered, because nothing was proven to reach the application.</summary>
    internal static byte[] CaptureScreenshot(ScreenshotTarget target, Deadline deadline)
    {
        try
        {
            return DispatchTarget(target, deadline);
        }
        catch (AdapterError error)
        {
            throw CoerceDisposition(error);
        }
    }

    private static byte[] DispatchTarget(ScreenshotTarget target, Deadline deadline)
    {
        deadline.EnsureRemaining();
        return target.Kind switch
        {
            ScreenshotTargetKind.Screen => CaptureScreen(target.Index, deadline),
            ScreenshotTargetKind.Display => CaptureDisplay(target.Index, target.Expected, deadline),
            ScreenshotTargetKind.ExactWindow => CaptureWindow(target.Window, deadline),
            _ => CaptureScreen(0, deadline),
        };
    }

    /// <summary>Marks a refusal whose delivery evidence is unknown as not delivered. Stronger
    /// evidence a capture already established is kept as it stands.</summary>
    internal static AdapterError CoerceDisposition(AdapterError error) =>
        error.Disposition.Delivery == Delivery.Unknown
            ? error.WithDisposition(DeliverySemantics.NotDelivered())
            : error;

    /// <summary>Reads the display at the index first, so the capture is checked against the
    /// monitor it was asked for, then captures that display.</summary>
    internal static byte[] CaptureScreen(int index, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var expected = DisplayAt(index, deadline);
        return CaptureDisplay(index, expected, deadline);
    }

    /// <summary>Captures a display inside the identity sandwich: the selection is proved
    /// before the capture, and the monitor is read again after it and re-proved, so a capture
    /// is never reported against a monitor that moved or vanished mid-operation.</summary>
    internal static byte[] CaptureDisplay(int index, DisplayInfo expected, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var selection = CaptureSelection(expected, deadline);
        deadline.EnsureRemaining();
        VerifyDisplayIdentity(index, expected, selection.Current);
        var captured = CaptureDisplaySubject(selection.RawIndex, deadline);
        deadline.EnsureRemaining();
        if (!TestHooks.SkipPostIdentity)
        {
            var after = DisplayAt(selection.RawIndex, deadline);
            deadline.EnsureRemaining();
            VerifyDisplayIdentity(index, selection.Current, after);
        }

        return captured;
    }

    /// <summary>Captures one window, refusing before any read when the target carries no process
    /// instance token, and re-proving the window identity after the capture so the image is
    /// attributable to the window that was asked for.</summary>
    internal static byte[] CaptureWindow(WindowInfo window, Deadline deadline)
    {
        if (window.ProcessInstance is null)
        {
            throw new AdapterError(
                ErrorCode.InvalidArgs,
                "Exact window screenshot requires a process instance token")
                .WithSuggestion("Refresh the target with 'list-windows', then retry")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        deadline.EnsureRemaining();
        var verified = ResolveWindowStrict(window, deadline);
        var handle = ParseHandle(verified.Id);
        if (handle == IntPtr.Zero)
        {
            throw new AdapterError(
                ErrorCode.InvalidArgs,
                "window id is not a Windows HWND-shaped id")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        var scale = ScaleForBounds(verified.Bounds, deadline);
        deadline.EnsureRemaining();
        var captured = CaptureWindowSubject(handle, scale, deadline);
        deadline.EnsureRemaining();
        if (!TestHooks.SkipPostIdentity)
        {
            if (TestHooks.ForcePostIdentityFailure)
            {
                throw new AdapterError(
                    ErrorCode.StaleRef,
                    "window identity could not be re-proved after capture")
                    .WithSuggestion("Run 'list-windows' to refresh window identifiers, then retry.")
                    .WithDisposition(DeliverySemantics.NotDelivered());
            }

            ResolveWindowStrict(verified, deadline);
        }

        return captured;
    }

    /// <summary>SEAM: `system::display::display_at` is not ported. A display read that turns an
    /// index into the monitor identity the sandwich verifies.</summary>
    private static DisplayInfo DisplayAt(int index, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: `system::display::capture_selection` is not ported. The raw enumeration
    /// index the backend captures, plus the monitor identity that selection proved.</summary>
    private static (int RawIndex, DisplayInfo Current) CaptureSelection(DisplayInfo expected, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: `system::display::verify_display_identity` is not ported. Compares the
    /// requested index against the monitor read, before the capture and again after it.</summary>
    private static void VerifyDisplayIdentity(int index, DisplayInfo expected, DisplayInfo current)
    {
        throw AdapterError.NotSupported("screenshot");
    }

    /// <summary>SEAM: `system::display::scale_for_bounds` is not ported. The effective scale of
    /// the monitor a window's bounds sit on, so the capture samples at the window's DPI.</summary>
    private static double ScaleForBounds(Rect? bounds, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: `system::capture_backend::capture_with_precedence` is not ported, and this
    /// is its monitor subject. The backend precedence order is the missing part.</summary>
    private static byte[] CaptureDisplaySubject(int rawIndex, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: `system::capture_backend::capture_with_precedence` is not ported, and this
    /// is its window subject: one handle captured at a scale.</summary>
    private static byte[] CaptureWindowSubject(IntPtr handle, double scale, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: `system::window::window_resolve::resolve_window_strict` is not ported. The
    /// strict re-identification that proves a window before capture and after it.</summary>
    private static WindowInfo ResolveWindowStrict(WindowInfo window, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: `system::window::window_ops::parse_handle` is not ported. A window id that
    /// is not HWND-shaped yields no handle, so the capture is refused before any read.</summary>
    private static IntPtr ParseHandle(string id) => IntPtr.Zero;
}

/// <summary>SEAM: `system::fixture::test_support` is not ported, so the test hooks are plain
/// flags. Skipping the post-capture identity check and forcing that check to fail let the
/// sandwich be exercised without a live display, the same lever the Rust hooks give the tests.
/// In the shipped build both flags stay false, as the non-test Rust build does.</summary>
internal static class TestHooks
{
    internal static bool SkipPostIdentity { get; set; }

    internal static bool ForcePostIdentityFailure { get; set; }
}

/// <summary>Which surface a screenshot target names.</summary>
internal enum ScreenshotTargetKind
{
    Screen,
    Display,
    ExactWindow,
    FullScreen,
}

/// <summary>The screenshot target: a display index, a display identity to verify, a window, or
/// the full screen. Only the fields the named kind uses are filled.</summary>
internal sealed record ScreenshotTarget(
    ScreenshotTargetKind Kind,
    int Index,
    DisplayInfo? ExpectedDisplay,
    WindowInfo? TargetWindow)
{
    /// <summary>The display identity the target expects. A target that names a display without it
    /// is refused before any capture runs.</summary>
    internal DisplayInfo Expected => ExpectedDisplay ?? throw new AdapterError(
        ErrorCode.InvalidArgs,
        "display screenshot needs the display identity it expects").Unsent();

    /// <summary>The window the target names. A target that names a window without it is refused
    /// before any capture runs.</summary>
    internal WindowInfo Window => TargetWindow ?? throw new AdapterError(
        ErrorCode.InvalidArgs,
        "window screenshot needs the window it names").Unsent();
}
