using System.Diagnostics;
using System.Globalization;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>
/// Modern capture through <c>Windows.Graphics.Capture</c> (WGC).
/// <para>
/// Availability is <c>GraphicsCaptureSession.IsSupported</c> only, never a build number (A22-1). One frame is
/// taken against the caller's deadline, and expiry is a backend failure, so the precedence order can fall back
/// to Legacy.
/// </para>
/// <para>
/// The frame is returned as the encoded PNG bytes, the carrier the screenshot orchestrator uses while the shared
/// <c>ImageBuffer</c> type is not ported. The width, height, and scale factor the Rust result carries stay with
/// the caller that reads them.
/// </para>
/// </summary>
internal static class CaptureModern
{
    private static readonly TimeSpan FramePollSlice = TimeSpan.FromMilliseconds(10);

    /// <summary>The same support predicate production capture and the availability probe use.</summary>
    /// <remarks>SEAM: the WGC activation surface is not ported, so <c>IsSupported</c> cannot be read.</remarks>
    internal static bool ModernIsSupported() => throw AdapterError.NotSupported("screenshot");

    /// <summary>Captures one window frame, sampled at the scale the caller read for the window's monitor.</summary>
    internal static byte[] CaptureWindow(IntPtr handle, double scale, Deadline deadline)
    {
        deadline.EnsureRemaining();
        EnsureSupported();
        var item = ItemForWindow(handle);
        return CaptureItem(item, scale, deadline);
    }

    /// <summary>Captures one display frame. The display is read first, so the item is created for the monitor
    /// the caller named and the frame is sampled at that monitor's scale.</summary>
    internal static byte[] CaptureDisplay(int index, Deadline deadline)
    {
        deadline.EnsureRemaining();
        EnsureSupported();
        var display = DisplayAt(index, deadline);
        var monitor = MonitorHandleFromId(display.Id);
        var item = ItemForMonitor(monitor);
        return CaptureItem(item, display.Scale, deadline);
    }

    /// <summary>Activation of the HWND and HMONITOR interop factory, read separately from
    /// <see cref="ModernIsSupported"/>: A22-1 measured <c>IsSupported == true</c> on build 17763 while this
    /// QueryInterface still returns <c>E_NOINTERFACE</c>.</summary>
    /// <remarks>SEAM: the interop factory is not ported.</remarks>
    internal static bool InteropIsAvailable() => throw AdapterError.NotSupported("screenshot");

    private static void EnsureSupported()
    {
        if (!ModernIsSupported())
        {
            throw new AdapterError(
                ErrorCode.ActionNotSupported,
                "modern capture is not available in this session");
        }
    }

    /// <summary>One frame of one capture item: size, device, frame pool, session, frame, texture, pixels, PNG.
    /// Every step is checked against the caller's deadline, and a frame that never arrives is the deadline's own
    /// timeout error, so the precedence order can fall back to Legacy.</summary>
    private static byte[] CaptureItem(IntPtr item, double scale, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var size = ReadItemSize(item, "read the capture item size");
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "capture item has a zero-sized frame");
        }

        var device = CreateCaptureDevice();
        deadline.EnsureRemaining();
        var pool = CreateFramePool(device, size);
        var session = CreateCaptureSession(pool, item);
        DisableCursorAndBorder(session);
        StartCapture(session, "start the capture session");

        if (ModernTestHooks.FailAfterStart)
        {
            throw new AdapterError(
                ErrorCode.ActionFailed,
                "modern capture forced failure after session start");
        }

        var frame = WaitForFrame(pool, deadline);
        var content = ReadFrameContentSize(frame, "read the frame content size");
        var width = Math.Max(content.Width, 0);
        var height = Math.Max(content.Height, 0);
        var surface = ReadFrameSurface(frame, "read the frame surface");
        var texture = TextureFromSurface(surface);
        var pixels = ReadTextureBgra(device, texture, width, height);
        if (width > int.MaxValue / 4)
        {
            throw new AdapterError(ErrorCode.Internal, "modern capture stride overflowed");
        }

        var stride = width * 4;
        return EncodeBgraToPng(pixels, width, height, stride, deadline);
    }

    /// <summary>Polls the frame pool, sleeping at most the poll slice, and returns the deadline's timeout error
    /// once the budget is spent. A frame that never arrives is a backend failure, never a partial capture.</summary>
    private static IntPtr WaitForFrame(IntPtr pool, Deadline deadline)
    {
        if (ModernTestHooks.HoldFrameBudgetMs > 0)
        {
            return HoldFramesUntil(deadline, ModernTestHooks.HoldFrameBudgetMs);
        }

        while (true)
        {
            deadline.EnsureRemaining();
            var frame = TryGetNextFrame(pool);
            if (frame != IntPtr.Zero)
            {
                return frame;
            }

            var slice = RemainingSlice(deadline, FramePollSlice);
            if (slice <= TimeSpan.Zero)
            {
                throw deadline.TimeoutError();
            }

            Thread.Sleep(slice);
        }
    }

    /// <summary>Holds frames for a budget armed inside the frame wait, after the capture session exists, so
    /// expiry is decided there and never by how long the setup before it took.</summary>
    private static IntPtr HoldFramesUntil(Deadline deadline, int budgetMs)
    {
        var held = Deadline.DetachedAfter(budgetMs);
        while (true)
        {
            deadline.EnsureRemaining();
            var slice = RemainingSlice(held, FramePollSlice);
            if (slice <= TimeSpan.Zero)
            {
                throw held.TimeoutError();
            }

            Thread.Sleep(slice);
        }
    }

    /// <summary>The part of the poll slice the deadline still allows, zero once the budget is spent.</summary>
    private static TimeSpan RemainingSlice(Deadline deadline, TimeSpan slice)
    {
        var remainingMs = (deadline.ExpiresAt - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
        if (remainingMs <= 0)
        {
            return TimeSpan.Zero;
        }

        return slice.TotalMilliseconds >= remainingMs
            ? TimeSpan.FromMilliseconds(remainingMs)
            : slice;
    }

    /// <summary>SEAM: <c>IGraphicsCaptureItemInterop.CreateForWindow</c> is not ported. The capture item for one
    /// window handle.</summary>
    private static IntPtr ItemForWindow(IntPtr handle) => throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>IGraphicsCaptureItemInterop.CreateForMonitor</c> is not ported. The capture item for one
    /// monitor handle.</summary>
    private static IntPtr ItemForMonitor(IntPtr monitor) => throw AdapterError.NotSupported("screenshot");

    /// <summary>Turns a display id back into the monitor handle it names. The id is the token the display
    /// inventory writes, so an id that is not that token is an internal fault, not a caller argument error.</summary>
    private static IntPtr MonitorHandleFromId(string id)
    {
        const string prefix = "monitor-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)
            || !ulong.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var raw))
        {
            throw new AdapterError(ErrorCode.Internal, "display id is not a monitor handle token");
        }

        return new IntPtr((nint)raw);
    }

    /// <summary>SEAM: <c>system::capture::capture_d3d::CaptureDevice::create</c> is not ported. The device the
    /// frame pool is created against.</summary>
    private static IntPtr CreateCaptureDevice() => throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>Direct3D11CaptureFramePool::CreateFreeThreaded</c> is not ported. A free-threaded pool of
    /// two B8G8R8A8 frames at the item size, closed when the capture ends.</summary>
    private static IntPtr CreateFramePool(IntPtr device, (int Width, int Height) size) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>GraphicsCaptureSession::CreateCaptureSession</c> is not ported. The session for one item,
    /// closed when the capture ends.</summary>
    private static IntPtr CreateCaptureSession(IntPtr pool, IntPtr item) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: the session option setters are not ported. Cursor capture and the colored border are both
    /// off, and a setter that fails is ignored, exactly as the Rust capture ignores it.</summary>
    private static void DisableCursorAndBorder(IntPtr session) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>GraphicsCaptureSession::StartCapture</c> is not ported.</summary>
    private static void StartCapture(IntPtr session, string context) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>GraphicsCaptureItem::Size</c> is not ported. The item frame size.</summary>
    private static (int Width, int Height) ReadItemSize(IntPtr item, string context) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>Direct3D11CaptureFrame::ContentSize</c> is not ported. The frame content size.</summary>
    private static (int Width, int Height) ReadFrameContentSize(IntPtr frame, string context) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>Direct3D11CaptureFrame::Surface</c> is not ported.</summary>
    private static IntPtr ReadFrameSurface(IntPtr frame, string context) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>Direct3D11CaptureFramePool::TryGetNextFrame</c> is not ported. No frame is a failure the
    /// caller polls against, never an empty frame.</summary>
    private static IntPtr TryGetNextFrame(IntPtr pool) => throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>system::capture::capture_d3d::texture_from_surface</c> is not ported.</summary>
    private static IntPtr TextureFromSurface(IntPtr surface) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>system::capture::capture_d3d::read_texture_bgra</c> is not ported, and so is
    /// <c>resource_balance</c>, the counter that bounds the capture objects one capture holds. The frame pool,
    /// session, frame, and texture each take one count and release it when the capture ends, which is the whole
    /// reason the Rust capture wraps them in guards.</summary>
    private static byte[] ReadTextureBgra(IntPtr device, IntPtr texture, int width, int height) =>
        throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>system::capture::png_codec::encode_bgra_to_png</c> is not ported. The BGRA frame encoded
    /// inside the caller's deadline.</summary>
    private static byte[] EncodeBgraToPng(
        byte[] pixels,
        int width,
        int height,
        int stride,
        Deadline deadline) => throw AdapterError.NotSupported("screenshot");

    /// <summary>SEAM: <c>system::display::display_at</c> is not ported. The display at an index, read inside the
    /// caller's deadline.</summary>
    private static DisplayInfo DisplayAt(int index, Deadline deadline) =>
        throw AdapterError.NotSupported("screenshot");
}

/// <summary>SEAM: <c>system::fixture::test_support</c> is not ported, so the two test levers are plain thread
/// flags. Failing after session start and holding frames for a budget let the frame wait and the session
/// teardown be exercised without a live capture, which is the same lever the Rust hooks give the tests. The Rust
/// hooks are compiled only into tests; these flags are internal and read only by tests.</summary>
internal static class ModernTestHooks
{
    [ThreadStatic]
    internal static bool FailAfterStart;

    [ThreadStatic]
    internal static int HoldFrameBudgetMs;
}
