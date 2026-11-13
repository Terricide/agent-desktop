using System;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>
/// Legacy display capture via BitBlt from the screen DC. Public index 0 is the primary display; the FullScreen
/// target maps here, never to the virtual-screen span. Monitor origins may be negative on multi-monitor desktops;
/// the blit source uses those coordinates as-is.
/// </summary>
internal static class CaptureDisplay
{
    /// <summary>
    /// Captures the display at the public enumeration index into a PNG buffer. Index 0 is the primary (FullScreen).
    /// Dimensions and scale come from the same enumeration the blit targets, never from literals.
    /// </summary>
    internal static ImageBuffer CaptureDisplayAt(int index, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var (bounds, scaleFactor) = DisplayAt(index, deadline);
        return CaptureDisplayBounds(bounds, scaleFactor, deadline);
    }

    internal static ImageBuffer CaptureDisplayBounds(Rect bounds, double scaleFactor, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var (width, height, originX, originY) = DisplayCaptureGeometry(bounds);
        deadline.EnsureRemaining();
        var pixels = BitbltScreenBgra(originX, originY, width, height);
        if (width > uint.MaxValue / 4)
        {
            throw new AdapterError(ErrorCode.Internal, "display capture stride overflowed");
        }
        var stride = width * 4;
        var png = EncodeBgraToPng(pixels, width, height, stride, deadline);
        return new ImageBuffer(png, ImageFormat.Png, width, height, scaleFactor);
    }

    internal static (uint Width, uint Height, int OriginX, int OriginY) DisplayCaptureGeometry(Rect bounds)
    {
        if (!double.IsFinite(bounds.Width)
            || !double.IsFinite(bounds.Height)
            || !double.IsFinite(bounds.X)
            || !double.IsFinite(bounds.Y))
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "Display bounds must be finite");
        }
        if (bounds.Width <= 0.0 || bounds.Height <= 0.0)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "Cannot capture a zero-area display");
        }
        var width = SaturatingToInt32(bounds.Width);
        var height = SaturatingToInt32(bounds.Height);
        if (width <= 0 || height <= 0)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "Cannot capture a zero-area display");
        }
        var unsignedWidth = (uint)width;
        var unsignedHeight = (uint)height;
        RejectOversizedCapture(unsignedWidth, unsignedHeight);
        return (unsignedWidth, unsignedHeight, SaturatingToInt32(bounds.X), SaturatingToInt32(bounds.Y));
    }

    /// <summary>
    /// Rust <c>as i32</c> semantics: truncates toward zero and saturates at the i32 range.
    /// </summary>
    private static int SaturatingToInt32(double value)
    {
        var truncated = global::System.Math.Truncate(value);
        if (truncated <= int.MinValue)
        {
            return int.MinValue;
        }
        if (truncated >= int.MaxValue)
        {
            return int.MaxValue;
        }
        return (int)truncated;
    }

    /// <summary>
    /// The display at the public enumeration index, read from the same enumeration the blit targets.
    /// </summary>
    private static (Rect Bounds, double ScaleFactor) DisplayAt(int index, Deadline deadline)
    {
        var display = Display.DisplayAt(index, deadline);
        return (display.Bounds, display.Scale);
    }

    /// <summary>
    /// The shared oversize check, run before any GDI resource is created for the region.
    /// </summary>
    private static void RejectOversizedCapture(uint width, uint height)
    {
        GdiSurface.RejectOversizedCapture(width, height);
    }

    /// <summary>
    /// Blits the screen region into a top-down 32-bit DIB section and copies the BGRA bytes out. The destination is
    /// the pair's memory DC, where the DIB section is already selected, and the source is the screen DC at the region
    /// origin, so the region is copied straight with no raster operation blending it.
    /// </summary>
    private static byte[] BitbltScreenBgra(int originX, int originY, uint width, uint height)
    {
        var surface = DibSection.Create(checked((int)width), checked((int)height));
        var blitted = GdiSurface.Native.BitBlt(
            surface.DcPair.MemoryDc,
            0,
            0,
            checked((int)width),
            checked((int)height),
            surface.DcPair.ScreenDc,
            originX,
            originY,
            SourceCopy);
        if (blitted == 0)
        {
            throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastWin32Error(), "BitBlt failed for display capture");
        }

        return surface.IntoBgra();
    }

    private static byte[] EncodeBgraToPng(byte[] pixels, uint width, uint height, uint stride, Deadline deadline)
    {
        return PngCodec.EncodeBgraToPng(pixels, width, height, stride, deadline);
    }

    private const uint SourceCopy = 0x00CC0020;

    /// <summary>
    /// A top-down 32-bit DIB section selected into the pair's memory DC, owning the pair, the bitmap, the selection
    /// it replaced, and the pixel block the platform allocated for the section.
    /// </summary>
    private sealed class DibSection : IDisposable
    {
        private DibSection(
            GdiSurface.GdiDcPair dcPair,
            IntPtr bitmap,
            IntPtr previous,
            IntPtr bits,
            int width,
            int height)
        {
            DcPair = dcPair;
            Bitmap = bitmap;
            Previous = previous;
            Bits = bits;
            Width = width;
            Height = height;
        }

        internal GdiSurface.GdiDcPair DcPair { get; }

        internal IntPtr Bitmap { get; }

        internal IntPtr Previous { get; }

        internal IntPtr Bits { get; }

        internal int Width { get; }

        internal int Height { get; }

        /// <summary>
        /// Creates the section and selects it into the pair's memory DC. A section whose pixel block is not returned
        /// fails before any blit is issued, and a selection that fails deletes the bitmap and releases only the
        /// balance that bitmap acquired.
        /// </summary>
        internal static DibSection Create(int width, int height)
        {
            var dcPair = GdiSurface.GdiDcPair.Create("display capture");
            var info = GdiSurface.TopDownBgraBitmapInfo(width, height);
            var bitmap = GdiSurface.Native.CreateDibSection(dcPair.MemoryDc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw HResult.AdapterErrorFromWin32((uint)Marshal.GetLastWin32Error(), "CreateDIBSection failed for display capture");
            }

            GdiSurface.GdiBalance.Acquire();
            var previous = GdiSurface.Native.SelectObject(dcPair.MemoryDc, bitmap);
            if (previous == IntPtr.Zero)
            {
                var error = HResult.AdapterErrorFromWin32((uint)Marshal.GetLastWin32Error(), "SelectObject failed for display capture");
                if (GdiSurface.Native.DeleteObject(bitmap) != 0)
                {
                    GdiSurface.GdiBalance.Release();
                }

                throw error;
            }

            return new DibSection(dcPair, bitmap, previous, bits, width, height);
        }

        /// <summary>
        /// Copies the section's pixel block out. The rows are contiguous at four bytes per pixel, so the byte count
        /// comes from the same non-wrapping arithmetic the blit depends on.
        /// </summary>
        internal byte[] IntoBgra()
        {
            var bytes = GdiSurface.CheckedBgraByteLen(Width, Height);
            var pixels = new byte[checked((int)bytes)];
            Marshal.Copy(Bits, pixels, 0, pixels.Length);
            return pixels;
        }

        /// <summary>
        /// Restores the memory DC's prior selection and releases the bitmap. The pair is torn down after, so the
        /// memory DC is still valid when the selection is restored.
        /// </summary>
        public void Dispose()
        {
            GdiSurface.RestoreSelectedBitmap(DcPair.MemoryDc, Previous, Bitmap);
            DcPair.Dispose();
        }
    }
}
