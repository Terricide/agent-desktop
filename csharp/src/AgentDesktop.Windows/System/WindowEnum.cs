using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows;

/// <summary>A top-level window as the enumeration pass records it: the identity-bearing handle
/// plus the geometry, visibility and ex-style facts the census filter judges, every field read
/// once off the same handle so the filter can cite its own evidence per criterion.</summary>
internal readonly record struct EnumeratedWindow(
    IntPtr Handle,
    bool Visible,
    bool Iconic,
    bool Cloaked,
    bool Tool,
    Rect Rect)
{
    internal bool IsZeroSized() => Rect.Width <= 0.0 || Rect.Height <= 0.0;
}

/// <summary>The top-level window walk, the census filter, and the window identifier the
/// inventory emits. Every predicate here is the one the shared adapter contract judges, not a
/// per-application rule: the filter is the four criteria the Windows census measured, and the
/// id is the handle the observation path resolves back.</summary>
internal static class WindowEnum
{
    private const int WindowLongExStyle = -20;
    private const uint ExtendedToolWindow = 0x00000080;
    private const int CloakedAttribute = 14;

    /// <summary>The census filter: a window an agent means is visible, has a non-zero rect,
    /// is not cloaked by the shell, and is not a tool window. The census measured 147
    /// top-level windows of which 137 invisible, 93 zero-size, 6 cloaked and 51 tool.</summary>
    internal static bool PassesFilter(EnumeratedWindow window) =>
        window.Visible && !window.IsZeroSized() && !window.Cloaked && !window.Tool;

    /// <summary>The handle a window identifier names, or nothing when the identifier does not
    /// name a handle. A recycled handle that names nothing is the honest absent answer.</summary>
    internal static IntPtr HandleFor(string windowId)
    {
        if (!windowId.StartsWith("w-"))
        {
            return IntPtr.Zero;
        }

        return long.TryParse(windowId[2..], out var value) ? new IntPtr(value) : IntPtr.Zero;
    }

    /// <summary>The window identifier the inventory emits and the observation path resolves.</summary>
    internal static string WindowId(IntPtr handle) => $"w-{handle.ToInt64()}";

    /// <summary>Enumerates every top-level window on the calling desktop. The visitor stops the
    /// walk by returning false, the documented contract. `EnumWindows` reports failure by
    /// returning zero, and a visitor that deliberately stopped also makes it return zero, so the
    /// two are told apart here: only a zero the visitor never asked for is an enumeration failure.</summary>
    internal static void EnumerateTopLevel(Func<EnumeratedWindow, bool> visit)
    {
        var pass = new EnumerationPass(visit);
        var pinned = GCHandle.Alloc(pass);
        try
        {
            if (Interop.EnumWindows(pass.Callback, (IntPtr)pinned) == 0 && !pass.StoppedEarly)
            {
                throw new AdapterError(ErrorCode.Internal, "EnumWindows failed to enumerate top-level windows");
            }
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>Whether the desktop shell cloaks a window right now. Two predicates read the
    /// same attribute for different reasons: the census filter excludes cloaked windows from
    /// the agent inventory, and a dismissed shell surface survives cloaked with
    /// `IsWindowVisible` still true.</summary>
    internal static bool IsCloaked(IntPtr window)
    {
        if (Interop.DwmGetWindowAttribute(window, CloakedAttribute, out var cloaked, sizeof(int)) != 0)
        {
            return false;
        }

        return cloaked != 0;
    }

    internal static bool IsToolWindow(IntPtr window) =>
        ((uint)Interop.GetWindowLongW(window, WindowLongExStyle) & ExtendedToolWindow) != 0;

    /// <summary>The window's screen rectangle, validated so a geometry the platform cannot
    /// report never reaches the envelope as a real rectangle.</summary>
    internal static Rect WindowRect(IntPtr window)
    {
        if (Interop.GetWindowRect(window, out var rect) == 0)
        {
            return new Rect(0, 0, 0, 0);
        }

        return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top).Validate();
    }

    /// <summary>The handle, its owner pid, or nothing when the handle addresses no window.
    /// A recycled handle that names nothing is the honest absent answer, not a zero pid.</summary>
    internal static uint? LiveWindowOwner(IntPtr window)
    {
        Interop.GetWindowThreadProcessId(window, out var pid);
        return pid == 0 ? null : pid;
    }

    /// <summary>Whether a handle is the desktop's foreground window right now.</summary>
    internal static bool IsForegroundWindow(IntPtr window) => Interop.GetForegroundWindow() == window;

    /// <summary>Reads the live title of a window handle. `GetWindowTextW` sends `WM_GETTEXT`
    /// when the window belongs to the calling process, so a same-process window whose thread
    /// never dispatches blocks the read outright. The pump probe runs first: a window that
    /// will not answer has no readable title, and identity still rests on the owner and
    /// generation checks that never touch the message queue.</summary>
    internal static string LiveWindowTitle(IntPtr window, uint probeMs)
    {
        if (Interop.SendMessageTimeoutW(window, 0, IntPtr.Zero, IntPtr.Zero, 0x0002, probeMs, out _) == IntPtr.Zero)
        {
            return "";
        }

        var buffer = new System.Text.StringBuilder(512);
        return Interop.GetWindowTextW(window, buffer, buffer.Capacity) <= 0 ? "" : buffer.ToString();
    }

    /// <summary>The window class of a handle, or nothing when the handle addresses nothing.
    /// The class is the evidence a frame-host window is recognised by, never a name.</summary>
    internal static string? WindowClassName(IntPtr window)
    {
        var buffer = new System.Text.StringBuilder(256);
        return Interop.GetClassNameW(window, buffer, buffer.Capacity) <= 0 ? null : buffer.ToString();
    }

    private sealed class EnumerationPass
    {
        private readonly Func<EnumeratedWindow, bool> _visit;
        private readonly Interop.EnumWindowsProc _callback;
        internal bool StoppedEarly;

        internal EnumerationPass(Func<EnumeratedWindow, bool> visit)
        {
            _visit = visit;
            _callback = Record;
        }

        internal Interop.EnumWindowsProc Callback => _callback;

        private bool Record(IntPtr window, IntPtr context)
        {
            var keepGoing = _visit(new EnumeratedWindow(
                window,
                Interop.IsWindowVisible(window) != 0,
                Interop.IsIconic(window) != 0,
                IsCloaked(window),
                IsToolWindow(window),
                WindowRect(window)));
            if (!keepGoing)
            {
                StoppedEarly = true;
            }

            return keepGoing;
        }
    }
}
