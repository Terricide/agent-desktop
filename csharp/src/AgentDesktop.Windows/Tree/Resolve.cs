using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows;

/// <summary>Re-identifies a captured ref by its role and stable identity fields over one
/// bounded walk of the window it was captured from. Zero live matches is a stale ref, two
/// or more is an ambiguous target, and control values and bounds are never identity.</summary>
internal static class ElementResolve
{
    private const int ResolveDepth = 50;

    internal static NativeHandle ResolveStrict(RefEntry entry, Client client, Deadline deadline)
    {
        if (!entry.Capabilities.UniqueAtCapture || !entry.Identity.IsCaptureUnique)
        {
            var reason = entry.Identity.UniquenessReason ?? entry.UniquenessReason ?? "ref was not unique at capture";
            throw new AdapterError(ErrorCode.AmbiguousTarget, $"Ref cannot be resolved strictly: {reason}")
                .WithSuggestion("Take a fresh snapshot or narrow the target with a locator")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        var handle = WindowEnum.HandleFor(entry.Scope.WindowId);
        if (handle == IntPtr.Zero)
        {
            throw StaleRef(entry);
        }

        var window = new WindowInfo
        {
            Id = entry.Scope.WindowId,
            App = entry.Process.Name,
            Pid = entry.Process.Pid,
            ProcessInstance = entry.Process.Instance,
        };

        VerifyWindowOrStale(handle, window, entry);

        using var source = UiaTreeSource.Create(client, false);
        using var rawRoot = ResolveWindowOrStale(client, handle, deadline, entry);
        var root = source.PrepareRoot(rawRoot);
        var matches = new List<Element>();
        try
        {
            Collect(source, root, entry.Identity, matches, 0, deadline);
            VerifyWindowOrStale(handle, window, entry);
        }
        catch
        {
            DisposeAll(matches);
            throw;
        }

        if (matches.Count == 0)
        {
            throw StaleRef(entry);
        }

        if (matches.Count > 1)
        {
            var count = matches.Count;
            DisposeAll(matches);
            throw new AdapterError(ErrorCode.AmbiguousTarget, "Multiple elements match the captured identity")
                .WithDetails(new JsonObject { ["matches"] = count })
                .WithSuggestion("Narrow the target with a locator")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return NativeHandle.New(matches[0]);
    }

    internal static RefEntryIdentity CandidateIdentity(NodeProperties properties, string windowKey)
    {
        var role = Roles.Resolve(properties);
        var fullDescription = properties.NonBlankText(PropertyIds.FullDescription);
        var helpText = properties.NonBlankText(PropertyIds.HelpText);
        return new RefEntryIdentity
        {
            Role = role,
            NativeId = properties.NonBlankText(PropertyIds.AutomationId),
            StableName = NameField(properties),
            Description = fullDescription ?? helpText,
            WindowKey = windowKey,
        };
    }

    /// <summary>The name as a field with certainty: a name UIA never returned is unread, a name
    /// it returned as empty means the element has no name, and a name it returned is the name.
    /// The first is an incomplete proof, so a ref captured with it cannot be re-identified.</summary>
    private static LocatorField<string> NameField(NodeProperties properties) =>
        properties.TextField(PropertyIds.Name).NonBlankField();

    private static void Collect(
        UiaTreeSource source,
        Element element,
        RefEntryIdentity wanted,
        List<Element> matches,
        int depth,
        Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            element.Dispose();
            throw WindowRoot.DeadlineExceeded(deadline).Unsent();
        }

        if (depth > ResolveDepth)
        {
            element.Dispose();
            return;
        }

        var properties = source.Read(element);
        var candidate = CandidateIdentity(properties, wanted.WindowKey);
        var matchedSelf = wanted.MatchesCapture(candidate);

        var children = new List<Element>();
        if (source.Children(element, false, children) >= 0)
        {
            foreach (var child in children)
            {
                Collect(source, child, wanted, matches, depth + 1, deadline);
            }
        }
        else
        {
            DisposeAll(children);
        }

        if (matchedSelf)
        {
            matches.Add(element);
        }
        else
        {
            element.Dispose();
        }
    }

    private static void VerifyWindowOrStale(IntPtr handle, WindowInfo window, RefEntry entry)
    {
        try
        {
            WindowRoot.VerifyOwner(handle, window);
        }
        catch (AdapterError error) when (error.Code == ErrorCode.WindowNotFound)
        {
            throw StaleRef(entry);
        }
    }

    private static Element ResolveWindowOrStale(Client client, IntPtr handle, Deadline deadline, RefEntry entry)
    {
        try
        {
            return WindowRoot.Resolve(client, handle, deadline);
        }
        catch (AdapterError error) when (error.Code == ErrorCode.WindowNotFound)
        {
            throw StaleRef(entry);
        }
    }

    private static void DisposeAll(List<Element> elements)
    {
        foreach (var element in elements)
        {
            element.Dispose();
        }
    }

    private static AdapterError StaleRef(RefEntry entry) =>
        new AdapterError(ErrorCode.StaleRef, "No live element matches the captured identity")
            .WithDetails(new JsonObject { ["native_id"] = entry.Identity.NativeId })
            .WithSuggestion("Take a fresh snapshot and re-identify the target")
            .WithDisposition(DeliverySemantics.NotDelivered());
}
