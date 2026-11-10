using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Menu;

/// <summary>
/// The located menu element for the <c>Menu</c> surface arm: the locating shapes of the detector's sources, returning
/// the element instead of a bool.
///
/// Each source locates on its own and <see cref="LocateMenu"/> composes them, so a source whose pool is empty answers
/// "not mine" and the next source is still asked. Composing them the other way round, where one source's empty pool
/// ends the whole search, is what made a Chromium DOM menu unrootable while the detector reported it open.
/// </summary>
internal static class MenuStateLocate
{
    /// <summary>
    /// The window a menu source located its element under. The observation roots at this window: a classic popup menu
    /// is its own <c>#32768</c> window and the element reports that handle, while a menu element with no native window
    /// of its own roots at the window it was located under. Reading the element's own handle is not ported, so this
    /// always takes the located window. That is sound for every source that resolves here: source B starts its probe
    /// at the tool window the element is inside, and a Chromium DOM menu element has no window of its own, so the
    /// located window is the right root for both. The classic menu-mode flag (source A) names no element at all, so
    /// the popup case never reaches this code. The located element itself is not carried, because nothing roots at
    /// it; the probe disposes it once it has answered.
    /// </summary>
    internal sealed record MenuLocation(nint Window)
    {
        /// <summary>The handle the observation roots at.</summary>
        internal nint RootHandle() => Window;
    }

    /// <summary>
    /// One source's answer, falling through to the next source only when this one located nothing. A source that did
    /// locate something short-circuits, so the fall-through costs nothing on the answering path.
    ///
    /// This is a named function rather than an inline conditional inside <see cref="LocateMenu"/> because it is the
    /// whole of the composition decision. The defect it exists to prevent is a later source that silently stops being
    /// consulted.
    /// </summary>
    internal static MenuLocation? FirstLocated(MenuLocation? located, global::System.Func<MenuLocation?> next)
    {
        return located ?? next();
    }

    /// <summary>
    /// The <c>Menu</c> surface arm, resolving the sources the detector answers with so a wait and the snapshot that
    /// follows it agree wherever they can.
    ///
    /// Source B (a menu-family element under a tool window) and source C (a Chromium DOM menu) are both resolved here,
    /// because both name an element this can root at. Every source is asked through <see cref="FirstLocated"/>, so no
    /// source's empty candidate pool can end the search on another's behalf.
    ///
    /// Source A cannot be resolved to an element, and this is the one place the two predicates diverge. The classic
    /// menu-mode flag reports that a Win32 menu is up without naming any element, so there is nothing to return. A
    /// <c>surface-appeared</c> wait therefore succeeds for such a menu while <c>snapshot --surface menu</c> reports it
    /// cannot be rooted.
    /// </summary>
    internal static MenuLocation? LocateMenu(ProcessId pid, Deadline deadline)
    {
        deadline.EnsureRemaining();
        MenuState.EnsureProcessExists(pid);
        deadline.EnsureRemaining();
        return FirstLocated(LocateInToolWindows(pid, deadline), () => LocateChromium(pid, deadline));
    }

    /// <summary>
    /// Source B's locating shape. A process owning no tool window has no tool-window menu, which is the whole meaning of
    /// the empty answer here. It says nothing about the other sources, and the automation client is deliberately not
    /// created for it, so a process this source cannot serve pays only the window enumeration.
    /// </summary>
    private static MenuLocation? LocateInToolWindows(ProcessId pid, Deadline deadline)
    {
        var candidates = MenuState.ToolWindowCandidates(pid);
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
            var handle = candidates[index];
            var element = MenuState.ProbeCandidateElement(handle, MenuState.IsMenuFamily);
            if (element is not null)
            {
                element.Dispose();
                return new MenuLocation(handle);
            }
        }
        return null;
    }

    private static MenuLocation? LocateChromium(ProcessId pid, Deadline deadline)
    {
        var window = MenuStateChromium.LocateChromiumDomMenu(pid, deadline);
        return window is { } value ? new MenuLocation(value) : null;
    }
}
