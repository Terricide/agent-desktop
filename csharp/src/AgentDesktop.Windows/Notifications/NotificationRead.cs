using System.Text.Json.Nodes;
using AgentDesktop.Core.Notifications;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Shell;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Notifications;

/// <summary>
/// Reads every notification entry the open Action Center presents, in tree order, with no index or filter applied.
/// Port of crates/windows/src/notifications/read.rs.
/// </summary>
/// <remarks>
/// Elements here are addressed by <c>AutomationId</c>, never by localized name: this host's shell text is localized
/// (es-ES) while these ids are framework-stable (A26-3). A property read on an element the caller already holds is not
/// a search, so a failed read surfaces as an error rather than an empty string.
/// </remarks>
internal static class NotificationRead
{
    internal const int MaxEntries = 256;

    internal const int MaxWalkDepth = 10;

    private const string TitleId = "Title";

    private const string ContentId = "Content";

    private const string AttributionId = "Attribution";

    private const string DismissButtonId = "DismissButton";

    private const string VerbButtonId = "VerbButton";

    private const string ClearAllButtonId = "ClearAllButton";

    /// <summary>
    /// Reads every notification entry the open center presents, in tree order, with no index or filter applied yet.
    /// </summary>
    internal static List<ListEntry> ReadEntries(Element root, Deadline deadline)
    {
        using var mainListView = FindById(root, Slots.TreeScopeDescendants, ShellSurfaceKinds.MainListView);
        var emptyState = EmptyStatePresent(root);
        return GateLandmarks(mainListView is not null, emptyState) switch
        {
            CenterShape.EmptyCenter => new List<ListEntry>(),
            _ => WalkFrom(mainListView ?? throw UnrecognizedCenterError(), deadline),
        };
    }

    /// <summary>
    /// Decides the center's shape from its landmarks. A center with neither landmark is a refusal, not an empty list.
    /// </summary>
    internal static CenterShape GateLandmarks(bool mainListViewPresent, bool emptyStatePresent)
    {
        if (mainListViewPresent)
        {
            return CenterShape.WalkList;
        }

        if (emptyStatePresent)
        {
            return CenterShape.EmptyCenter;
        }

        throw UnrecognizedCenterError();
    }

    internal static AdapterError UnrecognizedCenterError() =>
        new AdapterError(
            ErrorCode.PlatformNotSupported,
            "The open Action Center does not expose the notification list this adapter reads")
            .WithPlatformDetail(
                $"Windows build {ShellSurface.BuildNumber()} exposes no '{ShellSurfaceKinds.MainListView}' landmark under the open Action Center")
            .WithSuggestion(
                "This host's shell presents a notification tree shape this adapter does not recognize; "
                + "capture a snapshot of the surface for diagnosis instead of retrying")
            .WithDisposition(DeliverySemantics.NotDelivered());

    internal static AdapterError MissingClearAllError() =>
        new AdapterError(
            ErrorCode.PlatformNotSupported,
            "The open Action Center carries notifications but exposes no clear-all control")
            .WithPlatformDetail(
                $"Windows build {ShellSurface.BuildNumber()} exposes no '{ClearAllButtonId}' landmark alongside a populated notification list")
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>
    /// The first element with the dismiss <c>AutomationId</c> under an entry, or null when it has none.
    /// </summary>
    internal static Element? FindDismissButton(Element entry) =>
        FindById(entry, Slots.TreeScopeDescendants, DismissButtonId);

    /// <summary>
    /// Every verb button under an entry, in tree order.
    /// </summary>
    internal static List<Element> FindVerbButtons(Element entry) =>
        FindAllById(entry, Slots.TreeScopeDescendants, VerbButtonId);

    /// <summary>
    /// The center-wide clear-all button, or null when the center exposes none.
    /// </summary>
    internal static Element? FindClearAllButton(Element root) =>
        FindById(root, Slots.TreeScopeDescendants, ClearAllButtonId);

    /// <summary>
    /// Reads an element's <c>Name</c>. A failure to read it is an error, never an empty string, so a real notification's
    /// title cannot look blank rather than unreadable.
    /// </summary>
    internal static string NameOf(Element element)
    {
        var name = element.CurrentName();
        if (name.IsKnown())
        {
            return name.Value ?? string.Empty;
        }

        if (name.IsAbsent())
        {
            return string.Empty;
        }

        throw ReadFailure("read an element's name");
    }

    private static bool EmptyStatePresent(Element root)
    {
        foreach (var landmark in ShellSurfaceKinds.EmptyCenterLandmarks)
        {
            using var found = FindById(root, Slots.TreeScopeDescendants, landmark);
            if (found is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static List<ListEntry> WalkFrom(Element list, Deadline deadline)
    {
        var entries = new List<ListEntry>();
        Walk(list, null, 0, entries, deadline);
        return entries;
    }

    /// <summary>
    /// The depth-first walk that attributes entries to their source application.
    /// </summary>
    /// <remarks>
    /// A <c>Group</c> or <c>HeaderItem</c> carrying a direct <c>Title</c> child is a source group's header (A26-3 measured
    /// the per-source header as a Group-typed element under the list). Its title names the application for every entry
    /// met after it. A <c>ListItem</c> without a recognizable <c>Title</c> child is skipped rather than counted, so indices
    /// stay positions among recognized entries. Everything else is descended with the inherited group unchanged.
    /// Each child is owned by this walk until it is either transferred into an entry or disposed.
    /// </remarks>
    private static void Walk(Element element, string? group, int depth, List<ListEntry> entries, Deadline deadline)
    {
        if (entries.Count >= MaxEntries)
        {
            throw IncompleteTraversalError("Notification traversal reached its bounded entry limit");
        }

        if (depth > MaxWalkDepth)
        {
            throw IncompleteTraversalError("Notification traversal reached its bounded depth limit");
        }

        foreach (var child in Children(element))
        {
            var transferred = false;
            try
            {
                deadline.EnsureRemaining();
                var kind = ControlTypeOf(child);
                if (kind == ControlTypeIds.ListItem)
                {
                    var entry = EntryOf(child, group);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                        transferred = true;
                    }
                }
                else if (kind == ControlTypeIds.Group || kind == ControlTypeIds.HeaderItem)
                {
                    var header = DirectChildTitle(child);
                    var inherited = !string.IsNullOrEmpty(header) ? header : group;
                    Walk(child, inherited, depth + 1, entries, deadline);
                }
                else
                {
                    Walk(child, group, depth + 1, entries, deadline);
                }
            }
            finally
            {
                if (!transferred)
                {
                    child.Dispose();
                }
            }
        }
    }

    /// <summary>
    /// Builds one entry from a list item, or returns null when the item is not a notification row this reader claims.
    /// On a non-null return the entry takes ownership of <paramref name="item"/>; the caller must not dispose it.
    /// </summary>
    private static ListEntry? EntryOf(Element item, string? group)
    {
        string title;
        using (var titleElement = FindById(item, Slots.TreeScopeDescendants, TitleId))
        {
            if (titleElement is null)
            {
                return null;
            }

            title = NameOf(titleElement);
        }

        if (title.Length == 0)
        {
            return null;
        }

        string? body = null;
        using (var contentElement = FindById(item, Slots.TreeScopeDescendants, ContentId))
        {
            if (contentElement is not null)
            {
                var content = NameOf(contentElement);
                body = content.Length == 0 ? null : content;
            }
        }

        var actions = new List<string>();
        foreach (var verb in FindAllById(item, Slots.TreeScopeDescendants, VerbButtonId))
        {
            using (verb)
            {
                var name = NameOf(verb);
                if (name.Length > 0)
                {
                    actions.Add(name);
                }
            }
        }

        var appName = group ?? AttributionName(item);
        var info = new NotificationInfo
        {
            Index = 0,
            AppName = appName,
            Title = title,
            Body = body,
            Actions = actions,
        };
        return new ListEntry(info, item);
    }

    private static string AttributionName(Element item)
    {
        using var attribution = FindById(item, Slots.TreeScopeDescendants, AttributionId);
        return attribution is null ? string.Empty : NameOf(attribution);
    }

    private static string? DirectChildTitle(Element element)
    {
        using var title = FindById(element, Slots.TreeScopeChildren, TitleId);
        return title is null ? null : NameOf(title);
    }

    private static int ControlTypeOf(Element element)
    {
        var kind = element.CurrentControlType();
        if (kind.IsKnown())
        {
            return kind.Value;
        }

        throw ReadFailure("read an element's control type");
    }

    private static AdapterError IncompleteTraversalError(string message) =>
        new AdapterError(ErrorCode.AppUnresponsive, message)
            .WithDetails(new JsonObject { ["complete"] = false });

    private static AdapterError ReadFailure(string context) =>
        new AdapterError(ErrorCode.AppUnresponsive, $"UI Automation could not {context}")
            .WithDetails(new JsonObject { ["complete"] = false, ["retryable"] = true });

    /// <summary>
    /// SEAM: the children of <paramref name="element"/>, each returned as an owned <see cref="Element"/>. The Rust reader
    /// enumerates with a true condition and TreeScope::Children. The C# UIA layer has a child enumeration only inside
    /// <c>UiaTreeSource.Children</c> (private, tied to its walk cache), with no public binding this reader can call.
    /// </summary>
    internal static List<Element> Children(Element element) =>
        throw AdapterError.NotSupported("enumerate a notification element's children");

    /// <summary>
    /// SEAM: the first element matching an <c>AutomationId</c> property condition within <paramref name="scope"/>
    /// (<see cref="Slots.TreeScopeChildren"/> or <see cref="Slots.TreeScopeDescendants"/>), or null on absence. The Rust
    /// reader builds a property condition and calls FindFirst. The C# UIA layer has no CreatePropertyCondition or
    /// FindFirst binding; <see cref="Slots"/> defines only FindAll for element search.
    /// </summary>
    internal static Element? FindById(Element element, int scope, string automationId) =>
        throw AdapterError.NotSupported("search for an element by AutomationId");

    /// <summary>
    /// SEAM: every element matching an <c>AutomationId</c> property condition within <paramref name="scope"/>, in tree
    /// order. Same missing binding as <see cref="FindById"/>: no property-condition constructor exists in the C# UIA layer.
    /// </summary>
    internal static List<Element> FindAllById(Element element, int scope, string automationId) =>
        throw AdapterError.NotSupported("search for elements by AutomationId");
}
