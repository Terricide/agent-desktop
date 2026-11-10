using System;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>
/// Shared GDI screen and memory device-context pairing for the two Legacy capture backends.
/// <para>
/// The window backend selects a compatible bitmap into the memory DC and reads it back with
/// <c>GetDIBits</c>; the display backend selects a DIB section and reads its mapped bits directly. Those two
/// bitmap shapes stay distinct on purpose. What both surfaces do identically is open a screen DC, pair it with a
/// compatible memory DC, and tear the pair down in the same order. That pairing, the GDI-object balance counter,
/// and the Win32 error mapping live here once.
/// </para>
/// </summary>
internal static partial class GdiSurface
{
    private const ulong MaxCapturePixels = 64UL * 1024 * 1024;
    private const int BiRgb = 0;

    /// <summary>
    /// Refuses a capture whose pixel byte count would overflow the 32-bit arithmetic a raw GDI write depends on,
    /// before either backend creates any GDI resource for it. Window and display bounds are not clamped to a sane
    /// size by Windows, so this is reachable from ordinary input, not only adversarial input.
    /// </summary>
    /// <exception cref="AdapterError">Thrown with <see cref="ErrorCode.InvalidArgs"/> when the region is too large.</exception>
    internal static void RejectOversizedCapture(uint width, uint height)
    {
        var pixels = (ulong)width * height;
        var fits = pixels <= MaxCapturePixels && pixels * 4 <= int.MaxValue;
        if (fits)
        {
            return;
        }

        throw new AdapterError(
                ErrorCode.InvalidArgs,
                $"Cannot capture a {width}x{height} region: pixel buffer size overflows")
            .WithSuggestion("Capture a smaller window or region, then retry the screenshot");
    }

    /// <summary>
    /// Computes a BGRA pixel buffer's byte length with non-overflowing arithmetic.
    /// <para>
    /// <paramref name="width"/> and <paramref name="height"/> are validated non-negative by the caller before a
    /// surface is built from them. This still throws rather than wrapping, so a future caller that skips that
    /// validation fails closed instead of producing an undersized allocation.
    /// </para>
    /// </summary>
    /// <returns>The non-negative byte length.</returns>
    /// <exception cref="AdapterError">Thrown with <see cref="ErrorCode.Internal"/> when the length overflows.</exception>
    internal static long CheckedBgraByteLen(int width, int height)
    {
        var area = (long)width * height;
        if (area > long.MaxValue / 4 || area < long.MinValue / 4)
        {
            throw new AdapterError(ErrorCode.Internal, "capture buffer size overflowed");
        }

        var length = area * 4;
        if (length < 0)
        {
            throw new AdapterError(ErrorCode.Internal, "capture buffer size overflowed");
        }

        return length;
    }

    /// <summary>
    /// The <c>BITMAPINFO</c> for a 32-bit BGRA bitmap whose rows run top-down (negative height).
    /// </summary>
    internal static BitmapInfo TopDownBgraBitmapInfo(int width, int height) =>
        new(
            new BitmapInfoHeader(
                (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                width,
                -height,
                1,
                32,
                BiRgb,
                0,
                0,
                0,
                0,
                0),
            0);

    /// <summary>
    /// Restores the memory DC's prior GDI selection, deletes <paramref name="bitmap"/>, and decrements the balance
    /// counter acquired when the bitmap was selected.
    /// <para>
    /// Runs from <see cref="GdiDcPair.Dispose"/>, so it cannot throw. A bitmap still selected into a DC cannot be
    /// deleted, so if restoring <paramref name="previous"/> fails the balance counter is deliberately left
    /// un-decremented. The leak becomes observable as a non-zero <see cref="GdiBalance.Live"/> rather than silently
    /// disappearing behind an unconditional release.
    /// </para>
    /// </summary>
    internal static void RestoreSelectedBitmap(IntPtr memoryDc, IntPtr previous, IntPtr bitmap)
    {
        var restored = Native.SelectObject(memoryDc, previous);
        if (restored == IntPtr.Zero)
        {
            return;
        }

        var deleted = Native.DeleteObject(bitmap);
        if (deleted == 0)
        {
            return;
        }

        GdiBalance.Release();
    }

    /// <summary>
    /// Counts GDI objects this process holds open per thread. A pair or bitmap acquires on creation and releases on
    /// successful deletion, so a non-zero <see cref="Live"/> after teardown names a leaked object.
    /// </summary>
    internal static class GdiBalance
    {
        [ThreadStatic]
        private static int live;

        internal static int Live => live;

        internal static void Acquire() => live++;

        internal static void Release() => live--;

        internal static void Reset() => live = 0;
    }

    internal static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        internal static extern int DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern IntPtr CreateDibSection(IntPtr dc, ref BitmapInfo bitmapInfo, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern int BitBlt(IntPtr destinationDc, int x, int y, int width, int height, IntPtr sourceDc, int sourceX, int sourceY, uint rasterOperation);

        [DllImport("gdi32.dll")]
        internal static extern int DeleteObject(IntPtr obj);
    }
}
