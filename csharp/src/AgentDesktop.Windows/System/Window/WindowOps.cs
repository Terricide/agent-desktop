using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Window;

/// <summary>
/// The live top-level window inventory an agent means, per the A16-1 filter, and the handle, id and
/// foreground helpers that surround it.
/// </summary>
internal static class WindowOps
{
    private const uint GaRoot = 2;

    /// <summary>
    /// The filter the A16-1 census justifies: a window an agent means is visible, has a non-zero rect,
    /// is not cloaked by the shell, and is not a tool window. A16-1 measured 147 top-level windows of
    /// which 137 invisible, 93 zero-size, 6 cloaked and 51 tool.
    /// </summary>
    internal static bool PassesFilter(EnumeratedWindow window) =>
        window.Visible && !window.IsZeroSized() && !window.Cloaked && !window.Tool;

    /// <summary>
    /// The process facts one window needs: its owner's pid, the process-generation token and the image
    /// name that becomes <c>app</c>, all read from the same handle.
    /// </summary>
    internal static (ProcessId Pid, string? Token, string App)? ProcessFacts(nint handle)
    {
        var pid = WindowIdentity.LiveWindowOwner(handle);
        if (pid is not { } owner)
        {
            return null;
        }
        return (owner, TokenForPidOrNull(owner), ProcessImageNameOrEmpty(owner));
    }

    /// <summary>
    /// Reads the window class of a handle, or <c>null</c> when the handle addresses nothing or the read
    /// fails, the same unanswerable contract <c>LiveWindowOwner</c> draws for the process read beside it.
    /// </summary>
    internal static string? WindowClassName(nint handle)
    {
        var buffer = new char[256];
        var length = GetClassNameW(handle, buffer, buffer.Length);
        if (length <= 0)
        {
            return null;
        }
        return new string(buffer, 0, length);
    }

    /// <summary>
    /// The identity facts one listed window reports: its own process's, or, when the window is an
    /// application frame host carrying a live hosted application (A26-8), the hosted application's. The
    /// frame keeps the id, title, bounds and state; only <c>app</c> and <c>pid</c> descend to the hosted
    /// process, inside this one inventory pass rather than as a second enumeration.
    /// </summary>
    internal static (ProcessId Pid, string? Token, string App)? IdentityFacts(nint handle)
    {
        if (FrameIdentity.HostedApplicationPid(handle) is { } hosted)
        {
            return (hosted, TokenForPidOrNull(hosted), ProcessImageNameOrEmpty(hosted));
        }
        return ProcessFacts(handle);
    }

    /// <summary>
    /// Builds one <see cref="WindowInfo"/> from an enumerated window, carrying the identity facts
    /// already resolved for it.
    /// </summary>
    internal static WindowInfo WindowInfoFrom(
        EnumeratedWindow window,
        string title,
        (ProcessId Pid, string? Token, string App) facts,
        bool focused) =>
        new()
        {
            Id = WindowId(window.Handle),
            Title = title,
            App = facts.App,
            Pid = facts.Pid,
            ProcessInstance = facts.Token,
            Bounds = window.Rect,
            State = BuildWindowState(focused, window.Iconic, window.Visible),
        };

    /// <summary>
    /// The live top-level window inventory an agent means, per the A16-1 filter.
    /// Bounds every native call under <paramref name="deadline"/> and absorbs the transient mid-walk
    /// identity race internally, up to the shared listing-retry budget: a caller polling this entry
    /// point (<c>wait --window</c> chief among them) cannot retry a refusal itself, since
    /// <c>WindowNotFound</c> is not in its retryable set. A race that survives the whole budget still
    /// returns the same <c>WindowNotFound</c> refusal.
    /// </summary>
    internal static List<WindowInfo> ListWindowsLive(WindowFilter filter, Deadline deadline) =>
        ListingRetry.RetryTransientWindowRace(
            deadline,
            IsWindowIdentityRace,
            () => ListWindowsLiveOnce(filter, deadline));

    internal static bool IsWindowIdentityRace(AdapterError error) =>
        error.Code == ErrorCode.WindowNotFound;

    /// <summary>
    /// One single-shot walk of the live top-level window inventory.
    /// Verification re-runs on both sides of the read (the two-sided rule that macOS's
    /// <c>windows_from_records_with_focus</c> in <c>window_inventory.rs</c> carries): the owning process
    /// is re-checked after assembly, and a window whose process changed mid-listing fails the whole walk
    /// rather than emitting a half-identified entry.
    /// </summary>
    internal static List<WindowInfo> ListWindowsLiveOnce(WindowFilter filter, Deadline deadline)
    {
        var windows = new List<WindowInfo>();
        var focusedSeen = false;
        var appFilter = filter.App;
        AdapterError? failure = null;
        var seen = 0;

        WindowEnum.EnumerateTopLevel(window =>
        {
            try
            {
                if (seen > 0)
                {
                    deadline.EnsureRemaining();
                }
                seen++;
                if (!PassesFilter(window))
                {
                    return true;
                }
                if (IdentityFacts(window.Handle) is not { } facts)
                {
                    return true;
                }
                if (appFilter is not null && !AppInfo.AppNameMatches(facts.App, appFilter))
                {
                    return true;
                }
                var title = WindowIdentity.LiveWindowTitle(window.Handle) ?? string.Empty;
                var focused = !focusedSeen && IsForegroundWindow(window.Handle);
                if (filter.FocusedOnly && !focused)
                {
                    return true;
                }
                focusedSeen |= focused;
                var info = WindowInfoFrom(window, title, facts, focused);
                ReVerify(info);
                windows.Add(info);
                return true;
            }
            catch (AdapterError error)
            {
                failure = error;
                return false;
            }
        });

        if (failure is not null)
        {
            throw failure;
        }
        return windows;
    }

    /// <summary>
    /// Whether a handle is the desktop's foreground window right now.
    /// </summary>
    internal static bool IsForegroundWindow(nint handle) => GetForegroundWindow() == handle;

    /// <summary>
    /// Whether the handle or its root ancestor owns the desktop foreground. Child control HWNDs from
    /// WinForms/WPF are rarely foreground themselves even when their top-level window is.
    /// </summary>
    internal static bool IsRootForegroundWindow(nint handle)
    {
        if (handle == nint.Zero)
        {
            return false;
        }
        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return false;
        }
        if (foreground == handle)
        {
            return true;
        }
        var root = GetAncestor(handle, GaRoot);
        return root != nint.Zero && root == foreground;
    }

    /// <summary>
    /// Re-verifies a freshly listed window's identity per the two-sided rule, and both checks bind: the
    /// strict one covers app and title, which the stored path deliberately does not, while the stored one
    /// covers live handle ownership, which the strict path cannot see because its pid was derived from
    /// that same handle a moment earlier. A window destroyed or re-owned between assembly and verification
    /// fails the inventory rather than emitting a half-identified entry, so both failures throw.
    /// </summary>
    internal static void ReVerify(WindowInfo info)
    {
        var handle = ParseHandle(info.Id);
        if (WindowIdentityEvidence.FromInfo(handle, info) is not { } evidence)
        {
            return;
        }
        evidence.VerifyStrict();
        evidence.VerifyStored();
    }

    /// <summary>
    /// The native handle a <c>w-&lt;n&gt;</c> window id names. Every malformed id maps to the null handle.
    /// </summary>
    internal static nint ParseHandle(string id)
    {
        if (id.StartsWith("w-", StringComparison.Ordinal)
            && ulong.TryParse(id.AsSpan(2), out var value))
        {
            return unchecked((nint)value);
        }
        return nint.Zero;
    }

    /// <summary>
    /// The signed handle a <c>w-&lt;n&gt;</c> window id names, or <c>InvalidArgs</c> when the id is not
    /// that shape or names the null handle. The strict counterpart of <see cref="ParseHandle"/>.
    /// </summary>
    internal static long ParseWindowId(string id)
    {
        if (id.StartsWith("w-", StringComparison.Ordinal)
            && long.TryParse(id.AsSpan(2), out var handle)
            && handle > 0)
        {
            return handle;
        }
        throw new AdapterError(ErrorCode.InvalidArgs, "Malformed window identifier");
    }

    /// <summary>
    /// The inverse of <see cref="ParseHandle"/>: the <c>WindowInfo.Id</c> a live handle reports. Always
    /// through the unsigned width, so every id this code emits is one <see cref="ParseHandle"/> can
    /// round-trip.
    /// </summary>
    internal static string WindowId(nint handle) => $"w-{unchecked((nuint)handle)}";

    /// <summary>
    /// The window state shape every live <see cref="WindowInfo"/> shares: <c>Accessible</c> is always true
    /// for a window this process could observe at all, and <c>Minimized</c>/<c>Visible</c> are always
    /// known once observed, never absent.
    /// </summary>
    internal static WindowState BuildWindowState(bool isFocused, bool minimized, bool visible) =>
        new()
        {
            IsFocused = isFocused,
            Accessible = true,
            Minimized = minimized,
            Visible = visible,
        };

    private static string? TokenForPidOrNull(ProcessId pid) => ProcessIdentityCheck.TokenForPid(pid);

    private static string ProcessImageNameOrEmpty(ProcessId pid) =>
        ProcessIdentityCheck.ProcessImageName(pid) ?? string.Empty;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetClassNameW")]
    private static extern int GetClassNameW(nint hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetAncestor(nint hwnd, uint flags);
}
