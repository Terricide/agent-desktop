using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Notifications;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Notifications;

/// <summary>
/// Notification mutations: dismiss one entry, dismiss the center, and invoke a named action button. Each mutation
/// proves its effect by re-reading the Action Center, never by the invoke's own return value.
/// </summary>
/// <remarks>SEAM: the list, session, root and read helpers below are placeholders until they are ported.</remarks>
internal static class NotificationActions
{
    /// <summary>
    /// Removes exactly the identified notification and proves the removal by re-reading the surface.
    /// </summary>
    /// <remarks>The foreground floor fires first; the entry is re-read and identity-checked before its dismiss invoke.</remarks>
    internal static NotificationInfo DismissNotification(
        int index,
        string? appFilter,
        NotificationIdentity? identity,
        InteractionPolicy policy,
        Deadline deadline)
    {
        RequireForegroundPolicy(policy);
        var hwnd = OpenActionCenter(policy, deadline);
        try
        {
            return DismissImpl(index, appFilter, identity, hwnd, deadline);
        }
        finally
        {
            CloseActionCenter(hwnd);
        }
    }

    /// <summary>
    /// Clears the center and reports the outcome against the identity set captured before the clear.
    /// </summary>
    /// <remarks>Only captured members still present after the clear are failures; later arrivals are ignored.</remarks>
    internal static (List<NotificationInfo> Dismissed, List<string> Failures) DismissAll(
        string? appFilter,
        InteractionPolicy policy,
        Deadline deadline)
    {
        RequireForegroundPolicy(policy);
        var hwnd = OpenActionCenter(policy, deadline);
        try
        {
            return DismissAllImpl(appFilter, hwnd, deadline);
        }
        finally
        {
            CloseActionCenter(hwnd);
        }
    }

    /// <summary>
    /// Invokes the named action button on the identified notification.
    /// </summary>
    /// <remarks>An unknown action name is refused with <c>ACTION_NOT_SUPPORTED</c> before anything is invoked.</remarks>
    internal static ActionResult NotificationAction(
        int index,
        NotificationIdentity? identity,
        string actionName,
        InteractionPolicy policy,
        Deadline deadline)
    {
        RequireForegroundPolicy(policy);
        var hwnd = OpenActionCenter(policy, deadline);
        try
        {
            return ActionImpl(index, identity, actionName, hwnd, deadline);
        }
        finally
        {
            CloseActionCenter(hwnd);
        }
    }

    /// <summary>
    /// Removes the entry at <paramref name="index"/> with its own dismiss control, then verifies.
    /// </summary>
    /// <remarks>Clear-all substitutes for an ignored dismiss only when the target is the sole entry (A26-3).</remarks>
    private static NotificationInfo DismissImpl(
        int index,
        string? appFilter,
        NotificationIdentity? identity,
        nint hwnd,
        Deadline deadline)
    {
        var filter = BuildFilter(appFilter);
        var entry = LocateAndVerify(index, identity, filter, hwnd, deadline);
        var info = entry.Info;

        InvokeDismiss(entry.Element, index);
        var current = NotificationVerify.ReadSettlingWithout(new[] { info }, hwnd, filter, deadline);
        if (!NotificationVerify.EntryGone(info, current)
            && current.Count == 1
            && ClearAllStillTheSoleTarget(hwnd, info, filter, deadline))
        {
            using var root = RootFromHwnd(hwnd, deadline);
            var clear = FindClearAllButton(root);
            if (clear is not null)
            {
                InvokeElement(clear);
                current = NotificationVerify.ReadSettlingWithout(new[] { info }, hwnd, filter, deadline);
            }
        }

        if (!NotificationVerify.EntryGone(info, current))
        {
            throw NotificationVerify.DismissSurvivedError(index);
        }

        return info;
    }

    /// <summary>
    /// Re-verifies, immediately before the clear-all substitute fires, that the center still holds exactly the target.
    /// </summary>
    /// <remarks>A late arrival between the settle read and the invoke must never be cleared by the substitute.</remarks>
    private static bool ClearAllStillTheSoleTarget(
        nint hwnd,
        NotificationInfo target,
        NotificationFilter filter,
        Deadline deadline)
    {
        var current = NotificationList.ListInfos(filter, hwnd, deadline);
        return current.Count == 1 && NotificationVerify.SameIdentity(target, current[0]);
    }

    private static (List<NotificationInfo> Dismissed, List<string> Failures) DismissAllImpl(
        string? appFilter,
        nint hwnd,
        Deadline deadline)
    {
        var filter = BuildFilter(appFilter);
        var entries = NotificationList.ListEntries(filter, hwnd, deadline);
        if (entries.Count == 0)
        {
            return (new List<NotificationInfo>(), new List<string>());
        }

        var captured = entries.Select(entry => entry.Info).ToList();
        var invokeErrors = new List<(int Index, string Message)>();
        if (appFilter is not null)
        {
            DismissEachByFreshlyReadIdentity(captured, filter, hwnd, deadline, invokeErrors);
        }
        else
        {
            using var root = RootFromHwnd(hwnd, deadline);
            var clear = FindClearAllButton(root) ?? throw MissingClearAllError();
            InvokeElement(clear);
        }

        var current = NotificationVerify.ReadSettlingWithout(captured, hwnd, filter, deadline);
        var failures = DismissAllFailures(captured, current, invokeErrors);
        var dismissed = captured.Where(member => NotificationVerify.EntryGone(member, current)).ToList();
        return (dismissed, failures);
    }

    /// <summary>
    /// Dismisses each captured target through a freshly re-read live element, never through a stale handle.
    /// </summary>
    /// <remarks>Never falls back to the center-wide clear-all; each target is re-read by identity before invoking.</remarks>
    private static void DismissEachByFreshlyReadIdentity(
        IReadOnlyList<NotificationInfo> captured,
        NotificationFilter filter,
        nint hwnd,
        Deadline deadline,
        List<(int Index, string Message)> invokeErrors)
    {
        foreach (var target in captured)
        {
            var live = NotificationList.ListEntries(filter, hwnd, deadline);
            var current = live.FirstOrDefault(entry => NotificationVerify.SameIdentity(target, entry.Info));
            if (current is null)
            {
                continue;
            }

            try
            {
                InvokeDismiss(current.Element, current.Info.Index);
            }
            catch (AdapterError error)
            {
                invokeErrors.Add((target.Index, error.Message));
            }
        }
    }

    /// <summary>
    /// One failure line per captured member still present after the clear, carrying its recorded invoke error if any.
    /// </summary>
    /// <remarks>The settle read, not the invoke's return value, decides whether a member was dismissed.</remarks>
    private static List<string> DismissAllFailures(
        IReadOnlyList<NotificationInfo> captured,
        IReadOnlyList<NotificationInfo> current,
        IReadOnlyList<(int Index, string Message)> invokeErrors)
    {
        if (invokeErrors.Count == 0)
        {
            return NotificationVerify.SurvivorFailures(captured, current);
        }

        var failures = new List<string>();
        foreach (var member in captured)
        {
            if (NotificationVerify.EntryGone(member, current))
            {
                continue;
            }

            var recorded = invokeErrors.FirstOrDefault(error => error.Index == member.Index).Message;
            failures.Add(recorded is null
                ? $"#{member.Index}: notification from the captured set is still present"
                : $"#{member.Index}: {recorded}");
        }

        return failures;
    }

    private static ActionResult ActionImpl(
        int index,
        NotificationIdentity? identity,
        string actionName,
        nint hwnd,
        Deadline deadline)
    {
        var filter = new NotificationFilter();
        var entry = LocateAndVerify(index, identity, filter, hwnd, deadline);
        var original = entry.Info;
        InvokeNotificationAction(entry.Element, actionName, index);
        var current = NotificationVerify.ReadSettlingWithout(new[] { original }, hwnd, filter, deadline);
        return NotificationVerify.ActionChangedState(original, current)
            ? ActionResult.DeliveredVerified(actionName, new List<ActionStep>())
            : ActionResult.DeliveredUnverified(actionName);
    }

    private static void InvokeDismiss(Element entry, int index)
    {
        var button = FindDismissButton(entry) ?? throw MissingDismissError(index);
        InvokeElement(button);
    }

    private static void InvokeNotificationAction(Element entry, string actionName, int index)
    {
        var wanted = actionName.ToLowerInvariant();
        foreach (var verb in FindVerbButtons(entry))
        {
            if (NameOf(verb).ToLowerInvariant() == wanted)
            {
                InvokeElement(verb);
                return;
            }
        }

        throw UnknownActionError(actionName, index);
    }

    /// <summary>
    /// Invokes the element's UIA Invoke pattern, surfacing a classified UIA error when the read or invoke fails.
    /// </summary>
    private static void InvokeElement(Element element)
    {
        var readHr = element.CurrentPattern(Slots.PatternInvoke, out var pattern);
        if (readHr < 0 || pattern == IntPtr.Zero)
        {
            Calls.Release(pattern);
            throw UiaFailure.Error(readHr, "read the Invoke pattern");
        }

        try
        {
            var invokeHr = Calls.CallVoid(pattern, Slots.InvokePatternInvoke);
            if (invokeHr < 0)
            {
                throw UiaFailure.Error(invokeHr, "invoke a notification control");
            }
        }
        finally
        {
            Calls.Release(pattern);
        }
    }

    /// <summary>
    /// The floor every chrome-raising notification command answers to, checked before the center is raised.
    /// </summary>
    /// <remarks>Only <see cref="InteractionPolicy.Physical"/> permits the foreground to move.</remarks>
    internal static void RequireForegroundPolicy(InteractionPolicy policy)
    {
        if (policy == InteractionPolicy.Physical)
        {
            return;
        }

        throw AdapterError.NotSupported("Action Center interaction without foreground access");
    }

    /// <summary>
    /// The listing entry a mutation targets, checked against the identity the caller expects it to still carry.
    /// </summary>
    /// <remarks>The index is a position, not identity; a mismatch means the surface reordered under the caller.</remarks>
    private static ListEntry LocateAndVerify(
        int index,
        NotificationIdentity? identity,
        NotificationFilter filter,
        nint hwnd,
        Deadline deadline)
    {
        var entries = NotificationList.ListEntries(filter, hwnd, deadline);
        var entry = entries.FirstOrDefault(candidate => candidate.Info.Index == index)
            ?? throw new AdapterError(ErrorCode.NotificationNotFound, $"No notification at index {index}");
        NotificationIdentityCheck.Verify(index, identity, entry.Info);
        return entry;
    }

    private static NotificationFilter BuildFilter(string? appFilter) => new() { App = appFilter };

    private static AdapterError MissingDismissError(int index) =>
        new AdapterError(
                ErrorCode.ActionFailed,
                $"The notification at index {index} exposes no dismiss control to invoke")
            .WithSuggestion("Re-run list-notifications and retry with a freshly observed entry")
            .WithDisposition(DeliverySemantics.NotDelivered());

    private static AdapterError UnknownActionError(string actionName, int index) =>
        new AdapterError(
                ErrorCode.ActionNotSupported,
                $"Action '{actionName}' is not offered by the notification at index {index}")
            .WithSuggestion("Run list-notifications and use one of the entry's reported actions")
            .WithDisposition(DeliverySemantics.NotDelivered());

    private static AdapterError MissingClearAllError() => NotPorted("the clear-all button");

    private static nint OpenActionCenter(InteractionPolicy policy, Deadline deadline) =>
        throw NotPorted("open the Action Center session");

    private static void CloseActionCenter(nint hwnd) => throw NotPorted("close the Action Center session");

    private static Element RootFromHwnd(nint hwnd, Deadline deadline) =>
        throw NotPorted("resolve the Action Center root element");

    private static Element? FindClearAllButton(Element root) => throw NotPorted("find the clear-all button");

    private static Element? FindDismissButton(Element entry) => throw NotPorted("find the dismiss button");

    private static IReadOnlyList<Element> FindVerbButtons(Element entry) =>
        throw NotPorted("find the entry's action buttons");

    private static string NameOf(Element element) => throw NotPorted("read a control's name");

    private static AdapterError NotPorted(string what) => AdapterError.NotSupported($"SEAM: {what}");
}
