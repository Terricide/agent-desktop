using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>The window an observation roots at: which roots and surfaces this adapter reads,
/// the handle a "w-&lt;hwnd&gt;" id names, whether that handle still belongs to the process
/// the caller named, and its UI Automation root. Every refusal is an AdapterError with a
/// documented code, mirroring crates/windows/src/tree/observe.rs and automation.rs.</summary>
internal static class WindowRoot
{
    /// <summary>Longest the pump probe waits, independent of the operation deadline: the
    /// probe exists to avoid an unbounded block, so spending the whole budget on it would
    /// defeat it.</summary>
    internal const long PumpProbeCapMs = 2_000;

    private const uint NullMessage = 0;

    private const uint AbortIfHung = 0x0002;

    private static readonly SnapshotSurface[] Supported = { SnapshotSurface.Window };

    /// <summary>Refuses an element root, an application root, a process-owner window and any
    /// surface other than the window itself, before any UI Automation call.</summary>
    internal static WindowInfo Validate(ObservationRoot root, SnapshotSurface requested)
    {
        if (root is ObservationRoot.Element)
        {
            throw AdapterError.NotSupported("element observation root");
        }

        if (root is not ObservationRoot.Window window)
        {
            throw AdapterError.NotSupported("application observation root");
        }

        if (window.Info.IsProcessOwner())
        {
            throw AdapterError.AppHasNoWindow(window.Info.App);
        }

        foreach (var surface in new[] { root.Surface(), requested })
        {
            if (surface != SnapshotSurface.Window)
            {
                throw surface.Unavailable(Supported);
            }
        }

        return window.Info;
    }

    internal static IntPtr Handle(WindowInfo window)
    {
        if (!window.Id.StartsWith("w-", StringComparison.Ordinal)
            || !long.TryParse(window.Id.AsSpan(2), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var handle)
            || handle <= 0)
        {
            throw AdapterError.InvalidArgs("Malformed window identifier");
        }

        return new IntPtr(handle);
    }

    /// <summary>A recycled handle can name another process's window. The owner is read
    /// before and after the walk, so a tree is only returned for the process the caller
    /// named. A destroyed handle has no owner and fails the same way.</summary>
    internal static void VerifyOwner(IntPtr handle, WindowInfo window)
    {
        if (GetWindowThreadProcessId(handle, out var owner) == 0 || owner == 0 || owner != window.Pid.Value)
        {
            throw new AdapterError(ErrorCode.WindowNotFound, "The window's identity no longer matches its stored evidence")
                .WithSuggestion("Run 'list-windows' to refresh window identifiers, then retry.")
                .WithPlatformDetail($"HWND 0x{handle.ToInt64():X} is not owned by the stored process");
        }
    }

    /// <summary>ElementFromHandle sends WM_GETOBJECT to the window's thread, which a
    /// deadline cannot interrupt, so the deadline is checked before the window is touched
    /// and again after the call returns, as root_from_hwnd does. A handle that addresses
    /// nothing is a missing window; a live window that does not answer WM_NULL inside the
    /// probe cap is an unresponsive application, distinct so a destroyed window never looks
    /// hung.</summary>
    internal static Element Resolve(Client client, IntPtr handle, Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw DeadlineExceeded(deadline);
        }

        if (IsWindow(handle) == 0)
        {
            throw MissingRoot("The handle addresses no live window");
        }

        var probeMs = (uint)Math.Max(1, Math.Min(deadline.RemainingMs(), PumpProbeCapMs));
        if (SendMessageTimeoutW(handle, NullMessage, IntPtr.Zero, IntPtr.Zero, AbortIfHung, probeMs, out _) == IntPtr.Zero)
        {
            throw Unresponsive();
        }

        var hr = client.ElementFromHandle(handle, out var pointer);
        if (hr < 0 || pointer == IntPtr.Zero)
        {
            Calls.Release(pointer);
            throw RootFailure(hr);
        }

        var element = new Element(pointer);
        if (deadline.IsExpired())
        {
            element.Dispose();
            throw DeadlineExceeded(deadline);
        }

        return element;
    }

    /// <summary>An element that is gone is a missing window; any other failure, including a
    /// success that returned no element, is classified from its HRESULT.</summary>
    internal static AdapterError RootFailure(int hresult) =>
        hresult == UiaFailure.ElementNotAvailable
            ? MissingRoot(UiaFailure.Detail(hresult))
            : UiaFailure.Error(hresult, "resolve a window root");

    internal static AdapterError MissingRoot(string platformDetail) =>
        new AdapterError(ErrorCode.WindowNotFound, "UI Automation could not resolve a window root")
            .WithPlatformDetail(platformDetail)
            .WithSuggestion("List windows again and retry with a current window handle")
            .WithDetails(new JsonObject { ["kind"] = "window_root_missing", ["complete"] = true, ["retryable"] = false });

    internal static AdapterError Unresponsive() =>
        new AdapterError(ErrorCode.AppUnresponsive, "The window's thread is not dispatching messages")
            .WithSuggestion("Wait for the application to become responsive, then retry")
            .WithPlatformDetail("WM_GETOBJECT would block: the window thread did not answer WM_NULL")
            .WithDetails(new JsonObject { ["kind"] = "window_not_pumping", ["complete"] = false, ["retryable"] = true });

    /// <summary>The error core's Rust deadline raises when it is spent. The C# core's Deadline
    /// does not build one, so the adapter does, with the same message and details.</summary>
    internal static AdapterError DeadlineExceeded(Deadline deadline) =>
        AdapterError.Timeout("Operation exceeded its deadline").WithDetails(new JsonObject
        {
            ["kind"] = "deadline",
            ["timeout_ms"] = deadline.TimeoutMs,
            ["elapsed_ms"] = (Deadline.Now() - deadline.StartedAt) * 1000 / Stopwatch.Frequency,
        });

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern int IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
}
