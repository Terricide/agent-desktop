using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Refs;

/// <summary>
/// Frames for one tree: a frame is a node in the tree, and a frame node carries its children as
/// the node list. A ref-able element that keeps a provider element token anchors itself, and no
/// descendant needs a frame.
///
/// Each entry is one anchor, so the walk runs once over the tree. A node proven unique by the
/// capture is skipped, and its subtree is never walked, because resolution resolves it directly
/// against the capture proof and cannot reach a descendant. A node that can carry a ref and
/// carries identity evidence is offered as the boundary, so the walk stops below it and resolution
/// reaches everything below through that node. Each ancestor offered this way carries its own
/// proof, so a boundary below one that resolution can walk through is not lost. A node offered as
/// a boundary is not itself a ref, so the walk returns the whole subtree as uncovered, which is
/// what its ancestors need: a covered child proves no ref-able sibling, and an uncovered node
/// keeps every ancestor on the frame.
///
/// Port of <c>plan_skeleton_anchors</c> in <c>crates/core/src/refs/ref_alloc.rs</c>.
/// </summary>
internal static class SkeletonAnchors
{
    /// <summary>Compute the frame nodes for one tree, keyed by the path from the root. A ref-able
    /// node is keyed with its own path, and a skeleton-anchor frame keyed with its parent's path,
    /// so a ref-able child inside a skeleton frame is keyed by that frame.</summary>
    internal static HashSet<string> Plan(AccessibilityNode root, CaptureCensus census)
    {
        var anchors = new HashSet<string>();
        PlanNode(root, [], anchors, census);
        return anchors;
    }

    /// <summary>Whether a subtree needs a frame: a ref-able node not offered as a skeleton anchor,
    /// so a ref in the subtree has to be found by walking the tree. A node proven unique by the
    /// capture, and its subtree, needs no frame. A node offered as a boundary covers itself, which
    /// is why it returns the whole subtree as uncovered.</summary>
    private static bool PlanNode(
        AccessibilityNode node,
        List<int> path,
        HashSet<string> anchors,
        CaptureCensus census)
    {
        var uncovered = node.ChildrenCount is not null;
        for (var index = 0; index < node.Children.Count; index++)
        {
            path.Add(index);
            uncovered |= PlanNode(node.Children[index], path, anchors, census);
            path.RemoveAt(path.Count - 1);
        }

        if (!uncovered)
        {
            return false;
        }

        if (InteractiveRoles.QualifiesForRef(node) && Offered(node, census))
        {
            return false;
        }

        if (path.Count > 0 && CarriesIdentityEvidence(node) && Offered(node, census))
        {
            anchors.Add(PathKey(path));
            return false;
        }

        return true;
    }

    /// <summary>Whether a node can be trusted as a frame boundary: it keeps a provider element
    /// token, so identity does not have to be counted for it, or the capture proves it unique.</summary>
    private static bool Offered(AccessibilityNode node, CaptureCensus census) =>
        ElementToken.IsElementToken(node.Identity.RetainedObject) || census.Proves(node);

    /// <summary>Whether a node carries identity evidence, which is what a skeleton boundary needs
    /// to resolve: a native identifier, a stable name, a description, bounds, or a provider element
    /// token. A node carrying no evidence cannot be a skeleton anchor, so the node that carries the
    /// evidence keeps its own frame and every ancestor stays on the frame. A blank string is no
    /// evidence, and a field the adapter never read is not recorded as one, so a node read through a
    /// blank name cannot be a skeleton anchor. A value is not evidence: a text field's value is what
    /// the user typed into it, and two fields with the same label and different text are the same
    /// boundary.</summary>
    private static bool CarriesIdentityEvidence(AccessibilityNode node) =>
        ElementToken.IsElementToken(node.Identity.RetainedObject)
        || NonBlank(node.Identity.Name) is not null
        || NonBlank(node.Identity.Description) is not null
        || (node.Identity.NativeId is { } identifier && !string.IsNullOrWhiteSpace(identifier.Value))
        || node.Presentation.Bounds is not null;

    private static string? NonBlank(LocatorField<string> field)
    {
        var text = field.IsKnown() ? field.Value : null;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string PathKey(List<int> path) => string.Join('.', path);

    private static string? Known(LocatorField<string> field) =>
        field.IsKnown() ? field.Value : null;
}
