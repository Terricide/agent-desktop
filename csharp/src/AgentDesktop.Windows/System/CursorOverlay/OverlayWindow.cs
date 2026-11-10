using System;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The click-through, never-focused surface the overlay paints onto.
/// </summary>
/// <remarks>
/// The extended style set is load-bearing and measured rather than assumed: with <c>WS_EX_NOACTIVATE</c> the window
/// took the foreground at none of create, show, paint or move, and without it at three of those four.
/// <c>WS_EX_TRANSPARENT</c> is the click-through guarantee, and the same flag is why hit-testing cannot be used to
/// ask whether the overlay is on screen — that question is answered by a pixel.
/// <para>
/// <c>UpdateLayeredWindow</c> takes the whole surface at once, so no <c>WM_PAINT</c> is needed; the message loop
/// exists so the window keeps answering the system rather than being marked hung, and is pumped without blocking so
/// the control loop keeps its own pace. The window procedure handles nothing itself — topology changes such as a DPI
/// or display change are picked up by re-probing on the idle tick, not by handling a message here.
/// </para>
/// </remarks>
internal sealed class OverlayWindow : IDisposable
{
    private const string ClassName = "AgentDesktopCursorOverlay";
    private const string WindowTitle = "agent-desktop cursor overlay";
    private const int MaxPumpedMessages = 64;

    private static bool _registered;
    private static WndProc? _procAnchor;

    private IntPtr _handle;

    private OverlayWindow(IntPtr handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Creates the surface. A window that cannot be created is an internal error naming what failed, because the
    /// renderer has nothing to draw onto without it.
    /// </summary>
    internal static OverlayWindow Create()
    {
        Register();
        IntPtr instance = GetModuleHandleW(IntPtr.Zero);
        IntPtr handle = CreateWindowExW(
            ExtendedStyles,
            ClassName,
            WindowTitle,
            PopupStyle,
            0,
            0,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw Wide.Win32Error("The cursor overlay window could not be created");
        }

        return new OverlayWindow(handle);
    }

    /// <summary>
    /// Re-raises within the topmost band on every present, because the shell's own chrome is topmost too and the
    /// most recently raised window wins inside that band.
    /// </summary>
    internal void Raise() =>
        SetWindowPos(_handle, TopmostWindow, 0, 0, 0, 0, NoActivate | NoMove | NoSize);

    /// <summary>
    /// Places and paints the surface in one call. Position, size and pixels move together, so the window never shows
    /// a frame's content at the previous frame's position. An empty frame hides the window, which is how the overlay
    /// goes away between actions.
    /// </summary>
    internal void Present(int x, int y, int width, int height, uint[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length < (long)width * height)
        {
            ShowWindow(_handle, HideWindow);
            return;
        }

        using Dib? dib = Dib.Create(width, height);
        if (dib is null)
        {
            throw Wide.Win32Error("The overlay's drawing surface could not be made");
        }

        int count = width * height;
        unsafe
        {
            Span<uint> destination = dib.Pixels();
            for (int index = 0; index < count; index++)
            {
                destination[index] = pixels[index];
            }
        }

        PointWin32 position = new(x, y);
        PointWin32 source = new(0, 0);
        SizeWin32 size = new(width, height);
        BlendFunction blend = new();
        blend.BlendOp = SourceOver;
        blend.BlendFlags = 0;
        blend.SourceConstantAlpha = Opaque;
        blend.AlphaFormat = SourceAlpha;
        uint updated = UpdateLayeredWindow(
            _handle,
            dib.ScreenDc,
            ref position,
            ref size,
            dib.Dc,
            ref source,
            0,
            ref blend,
            AlphaBlendFlag);
        if (updated == 0)
        {
            throw Wide.Win32Error("The overlay could not be painted");
        }

        ShowWindow(_handle, ShowNoActivate);
    }

    /// <summary>
    /// Drains whatever the queue holds without waiting for more, so a quiet desktop costs nothing and a DPI change is
    /// still seen.
    /// </summary>
    internal void Pump()
    {
        Message message = new();
        for (int guard = 0; guard < MaxPumpedMessages; guard++)
        {
            if (!PeekMessageW(ref message, IntPtr.Zero, 0, 0, RemoveFromQueue))
            {
                return;
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static void Register()
    {
        if (_registered)
        {
            return;
        }

        _procAnchor = WindowProcedure;
        WndClassEx descriptor = new();
        descriptor.Size = (uint)Marshal.SizeOf<WndClassEx>();
        descriptor.Procedure = Marshal.GetFunctionPointerForDelegate(_procAnchor);
        descriptor.Instance = GetModuleHandleW(IntPtr.Zero);
        descriptor.ClassName = ClassName;
        RegisterClassExW(ref descriptor);
        _registered = true;
    }

    private static IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) =>
        DefWindowProcW(window, message, wParam, lParam);

    private const uint ExtendedStyles = Layered | Transparent | ToolWindow | NoActivateStyle | TopmostStyle;

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointWin32
    {
        public int X;
        public int Y;

        public PointWin32(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeWin32
    {
        public int Width;
        public int Height;

        public SizeWin32(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint MessageId;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Flags;
    }

    /// <summary>
    /// Field order is <c>WNDCLASSEXW</c> exactly: cbSize, style, lpfnWndProc, cbClsExtra, cbWndExtra, hInstance,
    /// hIcon, hCursor, hbrBackground, lpszMenuName, lpszClassName, hIconSm. A field out of order puts the class
    /// name in the menu-name slot, and the class registration fails.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Flags;
        public IntPtr Procedure;
        public int ClipWidth;
        public int ClipHeight;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? MenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string ClassName;

        public IntPtr IconSm;
    }

    private const uint Layered = 0x00080000;
    private const uint Transparent = 0x00000020;
    private const uint ToolWindow = 0x00000080;
    private const uint NoActivateStyle = 0x00000200;
    private const uint TopmostStyle = 0x00000010;
    private const uint PopupStyle = 0x00000000;
    private static readonly IntPtr TopmostWindow = new(-1);
    private const uint NoActivate = 0x0010;
    private const uint NoMove = 0x0002;
    private const uint NoSize = 0x0001;
    private const int HideWindow = 0;
    private const int ShowNoActivate = 8;
    private const uint AlphaBlendFlag = 0x00000002;
    private const byte SourceOver = 0x00;
    private const byte SourceAlpha = 0x03;
    private const byte Opaque = 255;
    private const bool RemoveFromQueue = true;

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WndClassEx descriptor);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(IntPtr moduleName);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyles,
        [MarshalAs(UnmanagedType.LPWStr)] string className,
        [MarshalAs(UnmanagedType.LPWStr)] string windowTitle,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr creationData);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr windowInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
    private static extern int ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessageW(
        ref Message message,
        IntPtr window,
        uint firstMessage,
        uint lastMessage,
        bool removeMessage);

    [DllImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessageW(ref Message message);

    [DllImport("user32.dll", EntryPoint = "UpdateLayeredWindow", SetLastError = true)]
    private static extern uint UpdateLayeredWindow(
        IntPtr window,
        IntPtr screenDc,
        ref PointWin32 position,
        ref SizeWin32 size,
        IntPtr sourceDc,
        ref PointWin32 source,
        uint colorKey,
        ref BlendFunction blend,
        uint flags);
}
