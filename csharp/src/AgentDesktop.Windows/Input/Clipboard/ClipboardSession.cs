using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>
/// Clipboard open/close lifetime, the hidden write owner window, and contention retry.
/// Writes and clears open with the process-owned HWND_MESSAGE window, because SetClipboardData fails with a NULL owner (A22).
/// Reads may open with a NULL owner. Contention exhaustion is TIMEOUT and reports GetOpenClipboardWindow without
/// deciding whether a NULL answer means a free clipboard or a windowless holder.
/// </summary>
internal sealed class ClipboardSession : IDisposable
{
    /// <summary>Chromium-shaped open budget: a handful of attempts a few milliseconds apart.</summary>
    internal const uint OpenClipboardRetryAttempts = 5;

    internal const int OpenClipboardRetryIntervalMs = 5;

    private static readonly TimeSpan OwnerPublishTimeout = TimeSpan.FromSeconds(5);

    private static readonly IntPtr HwndMessage = new(-3);

    private const uint WmQuit = 0x0012;

    private static readonly Lazy<IntPtr> OwnerWindow =
        new(PublishOwnerWindow, LazyThreadSafetyMode.ExecutionAndPublication);

    private bool closed;

    private ClipboardSession()
    {
    }

    internal static ClipboardSession OpenForRead(Deadline deadline)
    {
        return OpenWithOwner(IntPtr.Zero, deadline);
    }

    internal static ClipboardSession OpenForWrite(Deadline deadline)
    {
        var owner = OwnerHwnd();
        return OpenWithOwner(owner, deadline);
    }

    internal static IntPtr OwnerHwnd()
    {
        return OwnerWindow.Value;
    }

    public void Dispose()
    {
        if (closed)
        {
            return;
        }

        CloseClipboard();
        closed = true;
    }

    private static ClipboardSession OpenWithOwner(IntPtr owner, Deadline deadline)
    {
        EnsureRemainingNotDelivered(deadline);
        for (var attempt = 0u; attempt < OpenClipboardRetryAttempts; attempt++)
        {
            EnsureRemainingNotDelivered(deadline);
            if (OpenClipboard(owner))
            {
                return new ClipboardSession();
            }

            if (attempt + 1 == OpenClipboardRetryAttempts)
            {
                break;
            }

            var sleepFor = TimeSpan.FromMilliseconds(OpenClipboardRetryIntervalMs);
            var remaining = TimeSpan.FromMilliseconds(deadline.RemainingMs());
            if (remaining < sleepFor)
            {
                sleepFor = remaining;
            }

            if (sleepFor <= TimeSpan.Zero)
            {
                break;
            }

            Thread.Sleep(sleepFor);
        }

        throw ContentionTimeout(deadline);
    }

    private static AdapterError ContentionTimeout(Deadline deadline)
    {
        var holder = GetOpenClipboardWindow();
        var holderDetail = holder == IntPtr.Zero
            ? "GetOpenClipboardWindow returned NULL, which is ambiguous between a free clipboard and a windowless holder"
            : $"GetOpenClipboardWindow returned HWND 0x{holder.ToInt64():X}; the clipboard is held and that window is the reported holder";

        return deadline.TimeoutError()
            .WithPlatformDetail(holderDetail)
            .WithSuggestion("Retry after the current clipboard holder closes it; OpenClipboard permits only one holder")
            .WithDisposition(Disposition.NotDelivered());
    }

    private static void EnsureRemainingNotDelivered(Deadline deadline)
    {
        try
        {
            deadline.EnsureRemaining();
        }
        catch (AdapterError error)
        {
            throw NotDelivered(error);
        }
    }

    private static AdapterError NotDelivered(AdapterError error)
    {
        return error.WithDisposition(Disposition.NotDelivered());
    }

    private static IntPtr PublishOwnerWindow()
    {
        var published = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => OwnerThreadMain(published))
        {
            IsBackground = true,
            Name = "agent-desktop-clipboard-owner",
        };
        thread.Start();

        if (!published.Task.Wait(OwnerPublishTimeout))
        {
            throw OwnerFailure("clipboard owner thread never published its HWND");
        }

        var handle = published.Task.Result;
        if (handle == IntPtr.Zero)
        {
            throw OwnerFailure("CreateWindowExW produced no HWND_MESSAGE clipboard owner");
        }

        return handle;
    }

    private static AdapterError OwnerFailure(string message)
    {
        return AdapterError.Internal(message).WithDisposition(Disposition.NotDelivered());
    }

    private static void OwnerThreadMain(TaskCompletionSource<IntPtr> published)
    {
        var hwnd = CreateMessageOwnerWindow();
        published.TrySetResult(hwnd);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        while (true)
        {
            var status = GetMessageW(out var message, IntPtr.Zero, 0, 0);
            if (status == 0 || status == -1 || message.Message == WmQuit)
            {
                break;
            }

            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    private static IntPtr CreateMessageOwnerWindow()
    {
        return CreateWindowExW(
            0,
            "STATIC",
            "agent-desktop-clipboard-owner",
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            GetModuleHandleW(null),
            IntPtr.Zero);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetOpenClipboardWindow();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessageW(out Msg message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg message);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }
}
