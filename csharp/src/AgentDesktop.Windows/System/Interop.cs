using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;

namespace AgentDesktop.Windows;

/// <summary>The user32, dwmapi and kernel32 reads the window and process inventories need.
/// Native AOT has no COM interop and no managed window handle, so every call is a typed
/// P/Invoke. A call that fails is reported as an absent fact, never as a zero that reads
/// as a real window, a real process, or a real title.</summary>
internal static class Interop
{
    /// <summary>The documented `EnumWindows` callback contract: returning false stops the
    /// walk. The delegate is kept alive by the pinned context the walk passes to it.</summary>
    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr context);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("user32.dll")]
    internal static extern int EnumWindows(EnumWindowsProc callback, IntPtr context);

    [DllImport("user32.dll")]
    internal static extern int IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern int IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern int GetWindowLongW(IntPtr window, int index);

    [DllImport("user32.dll")]
    internal static extern int GetWindowRect(IntPtr window, out WinRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(IntPtr window, StringBuilder buffer, int maxLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(IntPtr window, StringBuilder buffer, int maxLength);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(int access, bool inherit, uint processId);

    [DllImport("kernel32.dll")]
    internal static extern uint GetLastError();

    [DllImport("kernel32.dll")]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint millis);

    [DllImport("kernel32.dll")]
    internal static extern int GetProcessTimes(IntPtr process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    internal static extern int CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int GetNamedPipeClientProcessId(SafeFileHandle pipe, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int GetNamedPipeServerProcessId(SafeFileHandle pipe, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetCurrentProcessId();

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder image, ref int size);
}

/// <summary>A FILETIME as the platform declares it: two 32-bit halves, 100-nanosecond ticks
/// since 1601. Passing each half as a separate out parameter hands the callee a 4-byte buffer
/// for an 8-byte struct, so the halves must travel together.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FileTime
{
    internal uint Low;
    internal uint High;
}

/// <summary>A screen rectangle as the platform reports it: two corners, not a size.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WinRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

/// <summary>One row of the ToolHelp process table, in the field order the platform declares.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ProcessEntry
{
    internal uint Size;
    internal uint Usage;
    internal uint ProcessId;
    internal nint DefaultHeapId;
    internal uint ModuleId;
    internal uint Threads;
    internal uint ParentProcessId;
    internal int PriorityClass;
    internal uint Flags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    internal string Name;
}
