using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>Window-handle validation for UI Automation resolution. Port of
/// crates/windows/src/tree/automation.rs.
///
/// The connection bound that holds is the client's own ConnectionTimeout (see Client.Create): a
/// call against a target that stops dispatching returns UIA_E_TIMEOUT rather than blocking. The
/// WM_NULL pump probe in WindowRoot.Resolve exists to turn an already-hung target into a clearer
/// APP_UNRESPONSIVE error sooner than waiting the connection timeout out.
///
/// A Deadline cannot interrupt a cross-thread SendMessage, so resolution checks it before the
/// window is touched and again after the call returns.</summary>
internal static class Automation
{
    /// <summary>Rejects a window handle that cannot address a window before it reaches the COM
    /// layer, so a null handle is an argument error rather than a COM failure.</summary>
    internal static void ValidateWindowHandle(IntPtr hwnd, Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw WindowRoot.DeadlineExceeded(deadline);
        }

        if (hwnd == IntPtr.Zero)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "Window handle is empty")
                .WithSuggestion("Pass a window handle obtained from a window listing");
        }
    }

    /// <summary>A live window whose thread is not dispatching messages. Distinct from a missing
    /// window: the handle is valid and retrying later can succeed, so it carries the explicit
    /// complete/retryable stamp every consumer keys on.</summary>
    internal static AdapterError UnresponsiveWindowError() => WindowRoot.Unresponsive();

    /// <summary>Whether a handle still answers the pump probe. Asked before the window root is
    /// resolved so the two failures stay distinct: a handle that addresses nothing is a missing
    /// window, while a live window that will not answer is an unresponsive application. Collapsing
    /// them would make a destroyed window look like a hung one and send a caller into a pointless
    /// retry.</summary>
    internal static bool WindowIsPumping(nint hwnd, ulong timeoutMs) =>
        Interop.SendMessageTimeoutW(
            hwnd,
            NullMessage,
            IntPtr.Zero,
            IntPtr.Zero,
            AbortIfHung,
            (uint)Math.Max(1UL, Math.Min(timeoutMs, uint.MaxValue)),
            out _) != IntPtr.Zero;

    /// <summary>The window root a handle addresses, bounded by the deadline. The handle is checked
    /// for existence before the pump probe so a destroyed window is reported as missing, and the
    /// probe runs before the COM call so a hung window is reported as unresponsive rather than
    /// waiting the connection timeout out. The client is created per call: a client whose calls
    /// cannot time out is refused, and observation is never run against one.</summary>
    internal static Element RootFromHwnd(nint hwnd, Deadline deadline)
    {
        var hr = Client.Create(out var client);
        if (client is null)
        {
            throw UiaFailure.Error(hr, "create a UI Automation client whose calls are bounded")
                .WithSuggestion("This build does not provide CUIAutomation8; the read is refused rather than run against a client that cannot time out");
        }

        using var owned = client;
        return WindowRoot.Resolve(client, hwnd, deadline);
    }

    private const uint NullMessage = 0;
    private const uint AbortIfHung = 0x0002;
}
