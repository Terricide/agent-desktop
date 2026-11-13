using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;
using global::System;
using global::System.Collections.Generic;

namespace AgentDesktop.Windows.System.Menu;

/// <summary>
/// Source C of the menu detector: the Chromium/Electron DOM-menu arm. Port of
/// crates/windows/src/system/menu/menu_state_chromium.rs.
///
/// Measured on a real Chromium host (A26-12): its context menu is a DOM menu rendered inside the application's own
/// window. No native menu tracking fires the classic flags, and the menu family surfaces under a non-tool window, so
/// the tool-window pool never sees it. The predicate probes the pid's visible, uncloaked, non-iconic, non-tool,
/// non-zero-sized root-level windows for a <c>Menu</c>/<c>MenuItem</c> element whose <c>FrameworkId</c> is the
/// Chromium framework's. This is a framework-family gate, not an application name. The measured reason it exists is
/// that a classic Win32 menu bar lives under exactly the same candidate pool at rest with framework <c>Win32</c>. A
/// persistent Chromium-framework menu bar would carry this framework id too. That shape was not measurable on the host
/// the source was measured on (its own menubar exposes no menu-family elements at rest), so it stays an explicitly
/// documented coverage bound rather than a claimed exclusion.
/// </summary>
internal static class MenuStateChromium
{
    /// <summary>
    /// The framework token Chromium's UIA provider reports for its elements, measured on the staged host (A26-12). The
    /// DOM menu's <c>Menu</c> and <c>MenuItem</c> elements all read this framework id, while a Win32 menu bar under the
    /// same candidate pool reads <c>Win32</c>.
    /// </summary>
    internal const string ChromiumFrameworkId = "Chrome";

    internal static bool ChromiumDomMenuReachable(ProcessId pid, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var candidates = ChromiumMenuCandidates(pid);
        if (candidates.Count == 0)
        {
            return false;
        }
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index > 0)
            {
                deadline.EnsureRemaining();
            }
            if (ProbeCandidate(candidates[index]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Source C's locating variant, so the surface arm can root where the detector fires. Without it a wait on
    /// <c>surface-appeared --surface menu</c> against a Chromium application succeeded and the
    /// <c>snapshot --surface menu</c> that followed answered <c>WINDOW_NOT_FOUND</c>, because only source B could
    /// locate anything.
    /// </summary>
    internal static IntPtr? LocateChromiumDomMenu(ProcessId pid, Deadline deadline)
    {
        deadline.EnsureRemaining();
        var candidates = ChromiumMenuCandidates(pid);
        if (candidates.Count == 0)
        {
            return null;
        }
        for (var index = 0; index < candidates.Count; index++)
        {
            if (index > 0)
            {
                deadline.EnsureRemaining();
            }
            var element = MenuState.ProbeCandidateElement(candidates[index], IsChromiumMenu);
            if (element is not null)
            {
                element.Dispose();
                return candidates[index];
            }
        }
        return null;
    }

    /// <summary>
    /// Source C's candidate pool: the mirror image of the tool-window pool source B searches. Only windows actually
    /// presented to the user are probed, because a menu the user cannot see cannot be open. This keeps hidden or cloaked
    /// helper windows of the same pid from firing the predicate at rest.
    /// </summary>
    internal static List<IntPtr> ChromiumMenuCandidates(ProcessId pid)
    {
        var handles = new List<IntPtr>();
        WindowEnum.EnumerateTopLevel(window =>
        {
            if (WindowEnum.LiveWindowOwner(window.Handle) == pid.Value
                && !window.Tool
                && window.Visible
                && !window.Iconic
                && !window.Cloaked
                && !window.IsZeroSized())
            {
                handles.Add(window.Handle);
            }
            return true;
        });
        return handles;
    }

    /// <summary>
    /// Whether one candidate window holds a Chromium-framework menu-family element: the menu family without
    /// <c>MenuBar</c> (a bar is not an open menu), gated on <c>FrameworkId</c> so a classic Win32 menu bar's own items
    /// under the same candidate windows never match.
    /// </summary>
    private static bool ProbeCandidate(IntPtr handle) =>
        ProbeChromiumElement(handle) is not null;

    /// <summary>The Chromium-framework menu-family element inside one candidate window, when the window holds one.</summary>
    private static Element? ProbeChromiumElement(IntPtr handle) =>
        MenuState.ProbeCandidateElement(handle, IsChromiumMenu);

    /// <summary>The Chromium gate: a <c>Menu</c> or <c>MenuItem</c> whose framework id is Chromium's. <c>MenuBar</c> is
    /// excluded - a bar is not an open menu - and the framework id is what keeps a classic Win32 menu bar's own items
    /// from matching under the same candidate pool.</summary>
    private static bool IsChromiumMenu(Element element)
    {
        var kind = element.CurrentControlType();
        if (!kind.IsKnown() || (kind.Value != ControlTypeIds.Menu && kind.Value != ControlTypeIds.MenuItem))
        {
            return false;
        }

        var framework = element.CurrentFrameworkId();
        return framework.IsKnown() && framework.Value == ChromiumFrameworkId;
    }
}
