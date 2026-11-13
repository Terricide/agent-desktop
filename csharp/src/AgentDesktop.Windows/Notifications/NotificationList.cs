using System.Collections.Generic;
using System.Linq;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Notifications;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Notifications;

/// <summary>
/// Lists the Action Center's notifications with the caller's filter applied.
/// </summary>
/// <remarks>
/// Opening the list raises shell chrome, so the floor lives here rather than
/// in core: a strict-headless caller is refused when the center is closed and
/// would have to be raised, before anything moves on the desktop, while a
/// caller whose policy permits the foreground change - or whose center is
/// already presented - proceeds. The session is one call: the center is
/// restored to its entry state afterwards, on every path.
/// </remarks>
internal static class NotificationList
{
    internal static List<NotificationInfo> ListNotifications(
        NotificationFilter filter,
        InteractionPolicy policy,
        Deadline deadline)
    {
        var session = ActionCenterSession.Open(policy, deadline);
        return ActionCenterSession.Close(session, () => ListFromCenter(filter, session.Hwnd, deadline));
    }

    private static List<NotificationInfo> ListFromCenter(NotificationFilter filter, nint hwnd, Deadline deadline) =>
        ListInfos(filter, hwnd, deadline);

    /// <summary>
    /// The mutation-side read: the same walk that produces the identities the
    /// mutating paths verify against, keeping the live element beside every info
    /// so an invoke lands on the entry that read produced.
    /// </summary>
    internal static List<ListEntry> ListEntries(NotificationFilter filter, nint hwnd, Deadline deadline)
    {
        var root = Automation.RootFromHwnd(hwnd, deadline);
        var entries = NotificationRead.ReadEntries(root, deadline);
        return Finalize(entries, filter);
    }

    /// <summary>
    /// The infos-only read the verification paths re-read with.
    /// </summary>
    internal static List<NotificationInfo> ListInfos(NotificationFilter filter, nint hwnd, Deadline deadline) =>
        ListEntries(filter, hwnd, deadline).Select(entry => entry.Info).ToList();

    /// <summary>
    /// Indices first, then predicates, then the limit - in that order.
    /// </summary>
    /// <remarks>
    /// An entry's index is its 1-based position among the recognized entries in
    /// tree order, so it stays meaningful to the mutating commands no matter what
    /// the filter kept. The app and text predicates run next, and the limit
    /// truncates last: a limit applied before filtering would hand back fewer
    /// entries than the caller asked to see whenever the filtered-out entries sit
    /// early in the tree. The elements in the entries travel beside the infos the
    /// pipeline kept, in the same order.
    /// </remarks>
    internal static List<ListEntry> Finalize(List<ListEntry> entries, NotificationFilter filter)
    {
        var survivors = OrderAndFilter(entries.Select(entry => entry.Info).ToList(), filter);
        var surviving = survivors.Select(info => entries[info.Index - 1]).ToList();
        for (var position = 0; position < surviving.Count; position++)
        {
            surviving[position].Info.Index = survivors[position].Index;
        }
        return surviving;
    }

    private static List<NotificationInfo> OrderAndFilter(List<NotificationInfo> infos, NotificationFilter filter)
    {
        for (var position = 0; position < infos.Count; position++)
        {
            infos[position].Index = position + 1;
        }
        var filtered = infos.Where(info => MatchesFilters(info, filter)).ToList();
        return filter.Limit is { } limit ? filtered.Take(limit).ToList() : filtered;
    }

    private static bool MatchesFilters(NotificationInfo info, NotificationFilter filter)
    {
        if (filter.App is { } app && !info.AppName.ToLowerInvariant().Contains(app.ToLowerInvariant()))
        {
            return false;
        }
        if (filter.Text is { } text)
        {
            var haystack = $"{info.Title} {info.Body ?? string.Empty} {info.AppName}".ToLowerInvariant();
            if (!haystack.Contains(text.ToLowerInvariant()))
            {
                return false;
            }
        }
        return true;
    }
}
