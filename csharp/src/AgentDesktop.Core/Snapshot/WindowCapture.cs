using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Snapshot;

/// <summary>The whole-window observation a capture proof counts against, and the depth cut the
/// snapshot presents. A capture proof counts over every node the window holds, so a read cut
/// short cannot prove any ref unique; the census needs the exhaustive read and the caller's tree
/// is a projection of it. Port of <c>whole_window</c> in
/// <c>crates/core/src/snapshot/window_capture.rs</c> and <c>is_exhaustive</c> and
/// <c>project_window</c> in <c>crates/core/src/locator/live_locator/observed_tree.rs</c>.</summary>
internal static class WindowCapture
{
    /// <summary>The depth a whole-window read walks to, so the census counts every node the
    /// window holds. Port of WHOLE_WINDOW_DEPTH in
    /// <c>crates/core/src/refs/resolve_saved_ref.rs</c>.</summary>
    internal const byte WholeWindowDepth = 50;

    /// <summary>Observes the whole window for the census to count against. A read cut short
    /// proves nothing, so it is dropped and the shallow walk stands alone.</summary>
    internal static AccessibilityNode? ReadWholeWindow(PlatformAdapter adapter, WindowInfo window,
        TreeOptions options, Deadline deadline)
    {
        var root = adapter.ObserveTree(
            new ObservationRoot.Window(window),
            new ObservationRequest(options.WithRefIdentityBounds() with { MaxDepth = WholeWindowDepth },
                deadline)).Root;
        return IsExhaustive(root) ? root : null;
    }

    /// <summary>Whether an observation read the whole window: complete, with no container whose
    /// descendants it left unread.</summary>
    internal static bool IsExhaustive(AccessibilityNode root) => !HasOmittedDescendants(root);

    private static bool HasOmittedDescendants(AccessibilityNode node)
    {
        if (node.SubtreeTruncated || (node.ChildrenCount > 0 && node.Children.Count == 0))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (HasOmittedDescendants(child))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Cuts a complete observation to the depth the caller asked for. The boundary keeps
    /// its real child count and marks its subtree truncated, so the presented tree says what it
    /// left out.</summary>
    internal static AccessibilityNode ProjectToDepth(AccessibilityNode node, byte depth)
    {
        if (depth == 0)
        {
            return node with
            {
                Children = new List<AccessibilityNode>(),
                ChildrenCount = node.Children.Count > 0 ? (uint)node.Children.Count : node.ChildrenCount,
                SubtreeTruncated = node.SubtreeTruncated || node.Children.Count > 0,
            };
        }

        var children = new List<AccessibilityNode>();
        foreach (var child in node.Children)
        {
            children.Add(ProjectToDepth(child, (byte)(depth - 1)));
        }

        return node with { Children = children };
    }
}
