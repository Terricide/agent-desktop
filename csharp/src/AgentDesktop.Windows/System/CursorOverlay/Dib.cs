using global::System;
using global::System.Runtime.InteropServices;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// An off-screen 32-bit surface GDI can draw into, and the teardown that goes with it.
/// Both the layered-window present and the label's glyph pass need a screen DC, a memory DC
/// compatible with it, and a top-down 32bpp DIB section selected into that memory DC. Holding
/// that once means the release sequence (restore the previously selected bitmap, delete the
/// bitmap, delete the memory DC, release the screen DC) has one order to be right about.
/// The height is negative on purpose: a positive height gives a bottom-up DIB, which draws the
/// whole surface mirrored.
/// </summary>
internal sealed class Dib : IDisposable
{
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;

    private IntPtr _screen;
    private IntPtr _memory;
    private IntPtr _bitmap;
    private IntPtr _previous;
    private IntPtr _bits;
    private int _count;
    private bool _disposed;

    private Dib()
    {
    }

    /// <summary>
    /// Returns <c>null</c> when the surface could not be made, with every handle taken so far
    /// already released. A caller that reads this as "draw nothing" is correct; there is nothing
    /// left to clean up.
    /// </summary>
    public static Dib? Create(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        long pixelCount = (long)width * height;
        if (pixelCount > int.MaxValue)
        {
            return null;
        }

        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr memory = CreateCompatibleDC(screen);
        BitmapInfo info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = BiRgb,
                SizeImage = 0,
                XPelsPerMeter = 0,
                YPelsPerMeter = 0,
                ClrUsed = 0,
                ClrImportant = 0,
            },
            Colors = 0,
        };

        IntPtr bitmap = CreateDIBSection(memory, ref info, DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
            return null;
        }

        IntPtr previous = SelectObject(memory, bitmap);
        Dib dib = new Dib
        {
            _screen = screen,
            _memory = memory,
            _bitmap = bitmap,
            _previous = previous,
            _bits = bits,
            _count = (int)pixelCount,
        };
        return dib;
    }

    /// <summary>The DC drawing calls target.</summary>
    public IntPtr Dc => _memory;

    /// <summary>
    /// The screen DC the memory one was made compatible with, which the layered-window present
    /// needs as its destination.
    /// </summary>
    public IntPtr ScreenDc => _screen;

    /// <summary>
    /// The bitmap's own storage, as pixels. The span is valid only while this instance is alive
    /// and not disposed.
    /// </summary>
    public unsafe Span<uint> Pixels() => new Span<uint>((uint*)_bits, _count);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SelectObject(_memory, _previous);
        DeleteObject(_bitmap);
        DeleteDC(_memory);
        ReleaseDC(IntPtr.Zero, _screen);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc,
        ref BitmapInfo bmi,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);
}
