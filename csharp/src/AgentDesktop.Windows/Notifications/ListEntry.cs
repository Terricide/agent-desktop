using AgentDesktop.Core.Notifications;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Notifications;

/// <summary>
/// One read notification and the live element it was read from.
/// </summary>
/// <remarks>
/// Mutating paths act on the same read that produced the identity they verified against: the element is what the
/// entry's own controls are located under at invoke time, so a mutation never re-resolves its target by a second,
/// possibly reordered read. The entry owns <see cref="Element"/>; a caller that discards an entry must dispose it.
/// </remarks>
internal sealed class ListEntry
{
    internal ListEntry(NotificationInfo info, Element element)
    {
        Info = info;
        Element = element;
    }

    internal NotificationInfo Info { get; }

    internal Element Element { get; }
}
