using System.Collections.Generic;
using System.Linq;

namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// The independent raise-response oracle behind the shell-surface skip convention.
/// </summary>
/// <remarks>
/// The product's own TIMEOUT envelopes cannot tell a declining desktop from a product whose resolver or raise
/// machinery regressed, so every immersive round-trip test would skip green. This oracle observes whether the
/// shell answered a raise at all, reading raw Win32 and UI Automation state directly: the foreground window, the
/// shell-host CoreWindow children of the desktop root, and the owner of the foreground window. The product's
/// resolver and raise path are never consulted.
///
/// Residual limits: a raise whose chord was dropped before the OS is indistinguishable from a shell-less desktop
/// by observation alone and reads as "did not respond". A desktop change the raise did not cause, inside the poll
/// window, reads as a response unless it is a shell-host CoreWindow surface (A26-1, A26-9).
/// </remarks>
internal static class RaiseOracle
{
    private static readonly string[] ShellHostImages =
    {
        "shellexperiencehost",
        "searchhost",
        "searchui",
        "searchapp",
        "startmenuexperiencehost",
    };

    private const string ShellCoreWindowClass = "Windows.UI.Core.CoreWindow";

    private static readonly global::System.TimeSpan ResponseWindow = global::System.TimeSpan.FromSeconds(3);

    private static readonly global::System.TimeSpan ResponsePoll = global::System.TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Captures the pre-raise state that <see cref="ShellRespondedToRaise"/> compares against.
    /// </summary>
    /// <returns>
    /// Null when the desktop's state cannot be read at all. A witness the harness cannot take is a desktop the
    /// oracle cannot speak about, so the caller's skip convention stands unchanged.
    /// </returns>
    internal static RaiseWitness? WitnessDesktop() => ReadDesktop();

    /// <summary>
    /// Whether the desktop moved away from the witness since the raise attempt. Polled across the response window
    /// so a shell that answers a beat late is still seen; a desktop that never responds answers false.
    /// </summary>
    internal static bool ShellRespondedToRaise(RaiseWitness witness)
    {
        var deadline = global::System.DateTime.UtcNow + ResponseWindow;
        while (true)
        {
            var current = ReadDesktop();
            if (current is not null && current.MovedSince(witness))
            {
                return true;
            }

            if (global::System.DateTime.UtcNow >= deadline)
            {
                return false;
            }

            global::System.Threading.Thread.Sleep(ResponsePoll);
        }
    }

    /// <summary>
    /// The closure shape the skip helper consumes. A witness that could not be captured keeps the skip
    /// convention: no evidence, no verdict.
    /// </summary>
    internal static bool RespondedSince(RaiseWitness? witness) =>
        witness is not null && ShellRespondedToRaise(witness);

    private static RaiseWitness? ReadDesktop()
    {
        var shellCoreWindowRootChildren = UiaShellCoreWindowRootChildren();
        if (shellCoreWindowRootChildren is null)
        {
            return null;
        }

        return new RaiseWitness(
            ForegroundHwnd(),
            ForegroundHwndIsShellCoreWindow(),
            shellCoreWindowRootChildren);
    }

    private static nint ForegroundHwnd() => global::AgentDesktop.Windows.Interop.GetForegroundWindow();

    /// <summary>
    /// Whether the foreground window is a shell-host-owned CoreWindow: the surface shape a raise presents, not
    /// merely shell chrome happening to be foreground.
    /// </summary>
    private static bool ForegroundHwndIsShellCoreWindow()
    {
        var handle = ForegroundHwnd();
        if (handle == 0)
        {
            return false;
        }

        var owner = global::AgentDesktop.Windows.WindowEnum.LiveWindowOwner(handle);
        if (owner is null)
        {
            return false;
        }

        var image = ProcessImageName(owner.Value);
        if (image is null || !IsShellHostImage(image))
        {
            return false;
        }

        var className = global::AgentDesktop.Windows.WindowEnum.WindowClassName(handle);
        return className is not null
            && string.Equals(className, ShellCoreWindowClass, global::System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The shell-host CoreWindow children at the desktop root, read straight off UI Automation and narrowed to the
    /// owning process and the window class, with no landmark or cloak matching.
    /// </summary>
    private static IReadOnlyList<nint>? UiaShellCoreWindowRootChildren()
    {
        var children = UiaDesktopChildren();
        if (children is null)
        {
            return null;
        }

        var handles = new List<nint>();
        foreach (var child in children)
        {
            if (!string.Equals(child.ClassName, ShellCoreWindowClass, global::System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (child.Handle == 0)
            {
                continue;
            }

            var image = ProcessImageName(child.ProcessId);
            if (image is null || !IsShellHostImage(image))
            {
                continue;
            }

            handles.Add(child.Handle);
        }

        handles.Sort();
        return handles;
    }

    private static bool IsShellHostImage(string image)
    {
        var stem = StripExeSuffix(image).ToLowerInvariant();
        return ShellHostImages.Any(host => stem == host);
    }

    private static string StripExeSuffix(string image) =>
        image.EndsWith(".exe", global::System.StringComparison.Ordinal) ? image[..^4] : image;

    private static string? ProcessImageName(uint processId) =>
        global::AgentDesktop.Windows.ProcessSnapshot.Capture().ImageNameFor(processId);

    /// <summary>
    /// Reads the desktop root's direct children from UI Automation. Not yet ported: the UIA client's
    /// root-element and find-all-children calls are not available as a C# helper.
    /// </summary>
    private static IReadOnlyList<UiaRootChild>? UiaDesktopChildren() =>
        throw AgentDesktop.Core.Response.AdapterError.NotSupported("raise_oracle");

    /// <summary>
    /// The desktop state a raise-response verdict is measured against, captured before the raise attempt.
    /// </summary>
    internal sealed record RaiseWitness(
        nint Foreground,
        bool ForegroundIsShellCoreWindow,
        IReadOnlyList<nint> ShellCoreWindowRootChildren)
    {
        internal bool MovedSince(RaiseWitness witness) =>
            (Foreground != witness.Foreground && ForegroundIsShellCoreWindow)
            || !ShellCoreWindowRootChildren.SequenceEqual(witness.ShellCoreWindowRootChildren);
    }

    /// <summary>
    /// One child of the UI Automation desktop root, with the properties the shell-host filter reads.
    /// </summary>
    internal sealed record UiaRootChild(string ClassName, nint Handle, uint ProcessId);
}
