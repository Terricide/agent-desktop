using System.Runtime.InteropServices;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows.System;

/// <summary>
/// Detects the process a top-level window hosts when that window is an application frame host frame carrying a live
/// hosted application (A26-8: the foreground window while a hosted Settings session is active).
/// </summary>
/// <remarks>
/// Detection requires both the <c>ApplicationFrameWindow</c> class and a child <c>Windows.UI.Core.CoreWindow</c> whose
/// owning process differs from the frame's. The class alone is insufficient: this desktop carries
/// <c>ApplicationFrameWindow</c> frames owned by the shell with no hosted <c>CoreWindow</c> beneath them, and treating
/// those as hosted would attribute a phantom application to the desktop. Suspension drops the hosted <c>CoreWindow</c>
/// while its frame survives uncloaked (measured on this build by activating the same single-instanced frame twice), so
/// a suspended application's frame reads as its frame host until the application resumes, and every identity that was
/// verified against the hosted pid fails closed for exactly as long.
/// <para>
/// <c>null</c> is every kind of "no hosted application here": a different class, an unreadable owner, no
/// <c>CoreWindow</c> child, and every <c>CoreWindow</c> child owned by the frame's own process.
/// </para>
/// </remarks>
internal static class FrameIdentity
{
    private const string FrameWindowClass = "ApplicationFrameWindow";

    private const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    /// <summary>
    /// Returns the process hosted by <paramref name="window"/>, or <c>null</c> when it hosts none.
    /// </summary>
    internal static ProcessId? HostedApplicationPid(IntPtr window)
    {
        if (WindowEnum.WindowClassName(window) != FrameWindowClass)
        {
            return null;
        }

        if (WindowEnum.LiveWindowOwner(window) is not { } framePid)
        {
            return null;
        }

        if (FindForeignOwnedCoreWindowChild(window, framePid) is not { } coreWindow)
        {
            return null;
        }

        return WindowEnum.LiveWindowOwner(coreWindow) is { } hostedPid ? ProcessId.From(hostedPid) : null;
    }

    /// <summary>
    /// The first child <c>CoreWindow</c> whose owning process differs from the frame's, walked in z-order through the
    /// frame's immediate children. A child whose owner cannot be read is passed over, not fatal: one raced child must
    /// not fail the walk while a later sibling could still answer.
    /// </summary>
    private static IntPtr? FindForeignOwnedCoreWindowChild(IntPtr frame, uint framePid)
    {
        var after = IntPtr.Zero;
        while (true)
        {
            var child = FindWindowExW(frame, after, CoreWindowClass, IntPtr.Zero);
            if (child == IntPtr.Zero)
            {
                return null;
            }

            if (WindowEnum.LiveWindowOwner(child) is { } pid && pid != framePid)
            {
                return child;
            }

            after = child;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
    private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, IntPtr windowName);
}
