using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.System.Window;

/// <summary>
/// Performs one window write and proves it landed: the native call, then the placement re-read the
/// operation's own read-back demands. Port of <c>window_op_impl</c> in
/// <c>crates/windows/src/system/window/window_op.rs</c>.
/// </summary>
/// <remarks>
/// Every native write here is queued across input queues — <c>SetWindowPos</c> carries
/// <c>SWP_ASYNCWINDOWPOS</c> and the show verbs use <c>ShowWindowAsync</c> — so the call returning
/// says nothing. The placement re-read is the only proof, and a write that never settles fails on the
/// deadline rather than being reported as delivered.
/// <para>
/// The two phases carry their own delivery. Anything refused in <c>PreWrite</c> is tagged
/// <c>not_delivered</c>, because no native call reached the window. Anything refused inside the
/// placement wait is tagged <c>delivered_unverified</c> by <c>WindowOpWait</c>, because the write
/// already went out. Ownership is checked on both sides for exactly this reason: a handle recycled
/// between the two checks is the case the second one catches, and its refusal has to admit the write
/// already happened.
/// </para>
/// </remarks>
internal static class WindowOpExecute
{
    private const double MaxDimension = 1_000_000.0;

    private const int SwMinimize = 6;

    private const int SwMaximize = 3;

    private const int SwRestore = 9;

    private const uint NoMove = 0x2;

    private const uint NoSize = 0x1;

    private const uint NoZOrder = 0x4;

    private const uint NoActivate = 0x10;

    private const uint AsyncWindowPos = 0x4000;

    /// <summary>Runs the write for <paramref name="op"/> against the stored window.</summary>
    internal static void Execute(WindowInfo win, WindowOp op, Deadline deadline)
    {
        try
        {
            PreWrite(win, op, deadline);
        }
        catch (AdapterError error)
        {
            throw error.WithDisposition(DeliverySemantics.NotDelivered());
        }
    }

    private static void PreWrite(WindowInfo win, WindowOp op, Deadline deadline)
    {
        deadline.EnsureRemaining();

        nint handle = WindowOps.ParseHandle(win.Id);
        if (handle == 0)
        {
            throw AdapterError.InvalidArgs("window id is not a Windows HWND-shaped id");
        }

        WindowIdentityEvidence? evidence = WindowIdentityEvidence.FromInfo(handle, win);
        if (evidence is null)
        {
            throw AdapterError.FromCode(
                ErrorCode.StaleRef,
                "window operation requires a process-instance token on the stored window");
        }

        evidence.VerifyStored();
        EnsureWindowIsPumping(handle);

        switch (op)
        {
            case WindowOp.Resize resize:
                Resize(handle, evidence, resize.Width, resize.Height, deadline);
                break;
            case WindowOp.Move move:
                MoveTo(handle, evidence, move.X, move.Y, deadline);
                break;
            case WindowOp.Minimize:
                ShowVerb(handle, evidence, WindowOpVerb.ShowVerb.Minimize, deadline);
                break;
            case WindowOp.Maximize:
                ShowVerb(handle, evidence, WindowOpVerb.ShowVerb.Maximize, deadline);
                break;
            case WindowOp.Restore:
                ShowVerb(handle, evidence, WindowOpVerb.ShowVerb.Restore, deadline);
                break;
            default:
                throw AdapterError.NotSupported("window_op");
        }
    }

    /// <summary>Refuses a write to a window that is not pumping messages. The native calls queue, so a
    /// hung window would block the command that follows it.</summary>
    private static void EnsureWindowIsPumping(nint handle)
    {
        if (WindowEnum.WindowIsResponsive(handle))
        {
            return;
        }

        throw AdapterError.FromCode(
            ErrorCode.AppUnresponsive,
            "Target window is not processing messages, so the window operation would block")
            .WithSuggestion("Wait for the application to recover, or close it with --force");
    }

    private static void Resize(nint handle, WindowIdentityEvidence evidence, double width, double height,
        Deadline deadline)
    {
        ValidateSize(width, height);
        int cx = RoundI32(width);
        int cy = RoundI32(height);

        EnsureOwnedBefore(evidence);
        SetWindowPos(handle, null, (cx, cy));

        WindowOpWait.WaitForPlacement(deadline, () =>
        {
            EnsureOwnedAfter(evidence);
            (int _, int _, int w, int h) = WindowRect(handle);
            return WithinTolerance(w, cx) && WithinTolerance(h, cy);
        });
    }

    private static void MoveTo(nint handle, WindowIdentityEvidence evidence, double x, double y,
        Deadline deadline)
    {
        ValidatePosition(x, y);
        int left = RoundI32(x);
        int top = RoundI32(y);

        EnsureOwnedBefore(evidence);
        SetWindowPos(handle, (left, top), null);

        WindowOpWait.WaitForPlacement(deadline, () =>
        {
            EnsureOwnedAfter(evidence);
            (int l, int t, int _, int _) = WindowRect(handle);
            return WithinTolerance(l, left) && WithinTolerance(t, top);
        });
    }

    private static void ShowVerb(nint handle, WindowIdentityEvidence evidence,
        WindowOpVerb.ShowVerb verb, Deadline deadline)
    {
        int command = verb switch
        {
            WindowOpVerb.ShowVerb.Minimize => SwMinimize,
            WindowOpVerb.ShowVerb.Maximize => SwMaximize,
            WindowOpVerb.ShowVerb.Restore => SwRestore,
            _ => throw AdapterError.NotSupported("window_op"),
        };

        EnsureOwnedBefore(evidence);
        if (ShowWindowAsync(handle, command) == 0)
        {
            throw HResult.Win32LastError("ShowWindowAsync failed");
        }

        WindowOpWait.WaitForPlacement(deadline, () =>
        {
            EnsureOwnedAfter(evidence);
            return WindowOpVerb.VerbIsSatisfied(verb, PlacementShowCmd(handle));
        });
    }

    /// <summary>The ownership check that runs before the write. A handle that already answers to a
    /// different process is refused, and the phase tags it as nothing reached the window.</summary>
    private static void EnsureOwnedBefore(WindowIdentityEvidence evidence)
    {
        if (!evidence.OwnsHandleNow())
        {
            throw AdapterError.FromCode(
                ErrorCode.WindowNotFound,
                "Target window handle changed ownership before window operation");
        }
    }

    /// <summary>The ownership check that runs after the write. The write already went out, so the refusal
    /// the placement wait tags carries the delivery that admits it.</summary>
    private static void EnsureOwnedAfter(WindowIdentityEvidence evidence)
    {
        if (!evidence.OwnsHandleNow())
        {
            throw AdapterError.FromCode(
                ErrorCode.WindowNotFound,
                "Target window handle changed ownership after window operation");
        }
    }

    private static (int Left, int Top, int Width, int Height) WindowRect(nint handle)
    {
        if (GetWindowRect(handle, out WinRect rect) == 0)
        {
            throw HResult.Win32LastError("GetWindowRect failed");
        }

        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static uint PlacementShowCmd(nint handle)
    {
        var placement = new WindowPlacement
        {
            Length = (uint)Marshal.SizeOf<WindowPlacement>(),
        };

        if (GetWindowPlacement(handle, ref placement) == 0)
        {
            throw HResult.Win32LastError("GetWindowPlacement failed");
        }

        return placement.ShowCmd;
    }

    private static void SetWindowPos(nint handle, (int X, int Y)? origin, (int Cx, int Cy)? size)
    {
        uint flags = AsyncWindowPos | NoZOrder | NoActivate;
        int x = 0, y = 0, cx = 0, cy = 0;

        if (origin is null)
        {
            flags |= NoMove;
        }
        else
        {
            (x, y) = origin.Value;
        }

        if (size is null)
        {
            flags |= NoSize;
        }
        else
        {
            (cx, cy) = size.Value;
        }

        if (SetWindowPosImport(handle, IntPtr.Zero, x, y, cx, cy, flags) == 0)
        {
            throw HResult.Win32LastError("SetWindowPos failed");
        }
    }

    private static void ValidateSize(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height)
            || width <= 0.0 || height <= 0.0
            || width > MaxDimension || height > MaxDimension)
        {
            throw AdapterError.InvalidArgs(
                "Window width and height must be finite, positive, and at most 1000000");
        }
    }

    private static void ValidatePosition(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)
            || Math.Abs(x) > MaxDimension || Math.Abs(y) > MaxDimension)
        {
            throw AdapterError.InvalidArgs(
                "Window coordinates must be finite and within -1000000..=1000000");
        }
    }

    private static int RoundI32(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
        {
            throw AdapterError.InvalidArgs(
                "Window geometry is outside the Win32 integer coordinate range");
        }

        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    private static bool WithinTolerance(int measured, int requested) =>
        Math.Abs(measured - requested) <= WindowOpPlacement.PlacementTolerancePx;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        internal uint Length;
        internal uint Flags;
        internal uint ShowCmd;
        internal int MinX;
        internal int MinY;
        internal int MaxX;
        internal int MaxY;
        internal int NormalLeft;
        internal int NormalTop;
        internal int NormalRight;
        internal int NormalBottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ShowWindowAsync(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    private static extern int SetWindowPosImport(nint hWnd, nint insertAfter, int x, int y, int cx,
        int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowPlacement(nint hWnd, ref WindowPlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowRect(nint hWnd, out WinRect rect);
}
