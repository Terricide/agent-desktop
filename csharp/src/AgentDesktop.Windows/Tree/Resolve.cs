using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows;

/// <summary>Re-identifies a captured ref by the provider identifier the capture recorded, counted
/// over one complete walk of the window it was captured from. Zero live matches is a stale ref, two
/// or more is an ambiguous target, and control values and bounds are never identity.</summary>
internal static class ElementResolve
{
    private const int ResolveDepth = 40;

    internal static NativeHandle ResolveStrict(RefEntry entry, Client client)
    {
        var wanted = entry.Identity.NativeId;
        if (wanted is null)
        {
            throw StaleRef(entry);
        }

        var handle = WindowEnum.HandleFor(entry.Scope.WindowId);
        if (handle == IntPtr.Zero)
        {
            throw StaleRef(entry);
        }

        using var source = UiaTreeSource.Create(client, false);
        var root = WindowRoot.Resolve(client, handle, Deadline.Standard());
        var matches = new List<Element>();
        Collect(source, root, wanted, matches, 0);
        root.Dispose();
        if (matches.Count == 0)
        {
            throw StaleRef(entry);
        }

        if (matches.Count > 1)
        {
            throw new AdapterError(ErrorCode.AmbiguousTarget, "Multiple elements match the captured identity")
                .WithDetails($"{{\"matches\":{matches.Count}}}")
                .WithSuggestion("Narrow the target with a locator")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return NativeHandle.New(matches[0]);
    }

    private static void Collect(UiaTreeSource source, Element element, string wanted, List<Element> matches, int depth)
    {
        if (depth > ResolveDepth)
        {
            return;
        }

        var identifier = element.AutomationId();
        if (identifier.Kind == LocatorFieldKind.Known && identifier.Value == wanted)
        {
            matches.Add(element);
            return;
        }

        var children = new List<Element>();
        if (source.Children(element, false, children) < 0)
        {
            return;
        }

        foreach (var child in children)
        {
            Collect(source, child, wanted, matches, depth + 1);
            child.Dispose();
        }
    }

    private static AdapterError StaleRef(RefEntry entry) =>
        new AdapterError(ErrorCode.StaleRef, "No live element matches the captured identity")
            .WithDetails($"{{\"native_id\":\"{entry.Identity.NativeId}\"}}")
            .WithSuggestion("Take a fresh snapshot and re-identify the target")
            .WithDisposition(DeliverySemantics.NotDelivered());
}
