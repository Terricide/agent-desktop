using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Runtime;
using global::System.Collections.Generic;

namespace AgentDesktop.Windows.System.Menu;

/// <summary>
/// The multi-pid menu-open check. It returns the same per-pid answer as the single-pid detector, but it shares one
/// classic-source <c>TH32CS_SNAPTHREAD</c> snapshot across all pids instead of taking one snapshot per pid.
/// </summary>
/// <remarks>
/// The snapshot enumerates every thread on the desktop regardless of which pid is asked about, so a multi-pid caller
/// pays for that walk once. Each pid then gets the same per-pid composition as the single-pid detector, in the same
/// short-circuit order: the tool-window probe runs only when the shared classic read did not already resolve the pid
/// true, and the Chromium DOM-menu probe runs only when the tool-window probe did not fire either. Every per-pid result
/// therefore matches what the single-pid detector would return for that pid alone, Chromium DOM menus included.
/// </remarks>
internal static class MenuStateMulti
{
    internal static Dictionary<ProcessId, bool> MenusOpenFor(IReadOnlyList<ProcessId> pids, Deadline deadline)
    {
        deadline.EnsureRemaining();
        foreach (var pid in pids)
        {
            EnsureProcessExists(pid);
        }

        deadline.EnsureRemaining();
        var results = ClassicMenuModeForMany(pids, deadline);
        foreach (var pid in pids)
        {
            if (results.GetValueOrDefault(pid))
            {
                continue;
            }

            deadline.EnsureRemaining();
            if (UiaMenuReachable(pid, deadline))
            {
                results[pid] = true;
                continue;
            }

            deadline.EnsureRemaining();
            results[pid] = MenuStateChromium.ChromiumDomMenuReachable(pid, deadline);
        }

        return results;
    }

    /// <summary>
    /// The multi-pid form of the classic source: one <c>TH32CS_SNAPTHREAD</c> snapshot walked once, with each thread's
    /// owner tested against every pid rather than one snapshot walk per pid. <c>GetGUIThreadInfo</c> is still read per
    /// thread through the same per-thread menu-mode check the single-pid source uses; only the snapshot enumeration is
    /// shared.
    /// </summary>
    /// <remarks>
    /// The walk never short-circuits here. Every thread on the desktop still owes a look, because a later thread could
    /// belong to a pid this walk has not yet resolved true.
    /// </remarks>
    private static Dictionary<ProcessId, bool> ClassicMenuModeForMany(IReadOnlyList<ProcessId> pids, Deadline deadline)
    {
        var found = new Dictionary<ProcessId, bool>();
        foreach (var pid in pids)
        {
            found[pid] = false;
        }

        ThreadWalk.WalkGuiThreads<object>(deadline, entry =>
        {
            var owner = ProcessId.From(entry.OwnerProcessId);
            if (found.TryGetValue(owner, out var reported) && !reported && ThreadReportsMenuMode(entry.ThreadId))
            {
                found[owner] = true;
            }

            return null;
        });

        return found;
    }

    /// <summary>The single-pid process-existence check, shared with the single-menu path. A pid that
    /// opens for query is alive; a rejected parameter means it is not.</summary>
    private static void EnsureProcessExists(ProcessId pid) => MenuState.EnsureProcessExists(pid);

    /// <summary>
    /// Seam: the UI Automation tool-window probe (<c>uia_menu_reachable</c>, source B) is not yet ported. Until it is, a
    /// call reports <c>NOT_SUPPORTED</c>.
    /// </summary>
    private static bool UiaMenuReachable(ProcessId pid, Deadline deadline) =>
        throw AdapterError.NotSupported("uia_menu_reachable");

    /// <summary>
    /// Seam: the per-thread <c>GetGUIThreadInfo</c> menu-mode read (<c>thread_reports_menu_mode</c>, source A) is not yet
    /// ported. Until it is, a call reports <c>NOT_SUPPORTED</c>.
    /// </summary>
    private static bool ThreadReportsMenuMode(uint threadId) =>
        throw AdapterError.NotSupported("thread_reports_menu_mode");
}
