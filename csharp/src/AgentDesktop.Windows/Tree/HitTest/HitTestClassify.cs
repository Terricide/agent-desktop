using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree.HitTest;

/// <summary>
/// Hit-element classification, the pre-probe guard ladder, and the budget the ancestor walks are
/// charged against.
///
/// Self or descendant gives <c>ReachesTarget</c>. Ancestor of the target gives <c>Unknown</c>
/// directly: unlike macOS, UIA's <c>ElementFromPoint</c> is desktop-global with no
/// application-scoped retry, so the arm that would only refine toward <c>Unknown</c> is the answer.
/// Chromium render-host panes land here when web content is not hit-addressable. Incomplete
/// ancestry walks give <c>Unknown</c>. Unrelated hits hand off to corroboration.
///
/// Every ancestor step is a cross-process call, so the operation deadline is consulted once per
/// step and a truncated walk is <c>Incomplete</c>, the same <c>Unknown</c> a failed step produces,
/// never an interception on partial evidence.
///
/// Rectangle membership is half-open on the right and bottom edges, the Win32 <c>RECT</c>
/// convention the <c>07-hittest</c> probe used: a point on the virtual screen's far edge addresses
/// the first pixel outside it, and A18-6 measured <c>ElementFromPoint</c> answering with the desktop
/// at exactly such coordinates.
/// </summary>
internal static class HitTestClassify
{
    internal enum Ancestry
    {
        Found,
        Absent,
        Incomplete,
    }

    internal enum HitClassification
    {
        ReachesTarget,
        AncestorOfTarget,
        Unrelated,
    }

    /// <summary>The pre-probe conditions that answer the hit test without probing.</summary>
    internal enum PreProbeGuard
    {
        ZeroArea,
        IconicRoot,
        OutsideVirtualScreen,
        OutsideTargetBounds,
    }

    /// <summary>Every <see cref="PreProbeGuard"/> value, for tests that must cover the whole ladder.</summary>
    internal static readonly PreProbeGuard[] AllPreProbeGuards =
    [
        PreProbeGuard.ZeroArea,
        PreProbeGuard.IconicRoot,
        PreProbeGuard.OutsideVirtualScreen,
        PreProbeGuard.OutsideTargetBounds,
    ];

    /// <summary>
    /// One ancestor walk's per-step inputs: element comparison, keying for the cycle guard, the
    /// parent step itself, and the budget every step is charged against.
    /// <c>ParentOf</c> returns <c>Succeeded = false</c> for a failed step, and
    /// <c>Succeeded = true</c> with a null <c>Parent</c> for a node that has no parent.
    /// </summary>
    internal sealed record AncestryWalk<TNode>(
        Func<TNode, TNode, bool> SameElement,
        Func<TNode, NodeKey> Identity,
        Func<TNode, (bool Succeeded, TNode? Parent)> ParentOf,
        Deadline Deadline)
        where TNode : class;

    /// <summary>
    /// The whole pre-probe ladder, over inputs the caller has already read.
    /// <c>null</c> means the probe may run.
    /// </summary>
    internal static PreProbeGuard? PreProbeDecision(Rect bounds, Point point, Rect screen, bool rootIconic)
    {
        if (!RectHasArea(bounds))
        {
            return PreProbeGuard.ZeroArea;
        }

        if (rootIconic)
        {
            return PreProbeGuard.IconicRoot;
        }

        if (!PointInRect(point, screen) || IntersectRects(bounds, screen) is null)
        {
            return PreProbeGuard.OutsideVirtualScreen;
        }

        if (!PointInRect(point, bounds))
        {
            return PreProbeGuard.OutsideTargetBounds;
        }

        return null;
    }

    /// <summary>
    /// Every guard trip is absence of evidence: degenerate geometry is not hit-testable, a minimized
    /// root's coordinates are stale, and a point off the virtual screen is answered by the desktop.
    /// None of them invents an interception.
    /// </summary>
    internal static HitTestResult ResultForGuard(PreProbeGuard guard) => HitTestResult.Unknown;

    /// <summary>
    /// A walk the deadline truncated, a cycle, or a failed step all leave the ancestor relation
    /// unproven, and an unproven relation is never an occluder.
    /// </summary>
    internal static HitTestResult ResultForIncompleteWalk() => HitTestResult.Unknown;

    internal static HitClassification ClassifyRelation(bool reachesTarget, bool isAncestorOfTarget)
    {
        if (reachesTarget)
        {
            return HitClassification.ReachesTarget;
        }

        if (isAncestorOfTarget)
        {
            return HitClassification.AncestorOfTarget;
        }

        return HitClassification.Unrelated;
    }

    internal static bool PointInRect(Point point, Rect bounds) =>
        point.X >= bounds.X
        && point.Y >= bounds.Y
        && point.X < bounds.X + bounds.Width
        && point.Y < bounds.Y + bounds.Height;

    /// <summary>
    /// The overlap of two rectangles, or null when they share no area. Uses <c>Math.Max</c> and
    /// <c>Math.Min</c>; the inputs are validated finite geometry, so the NaN ordering difference
    /// from Rust's <c>f64::max</c> cannot arise.
    /// </summary>
    internal static Rect? IntersectRects(Rect left, Rect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.X + left.Width, right.X + right.Width);
        var bottomEdge = Math.Min(left.Y + left.Height, right.Y + right.Height);
        if (rightEdge > x && bottomEdge > y)
        {
            return new Rect(x, y, rightEdge - x, bottomEdge - y);
        }

        return null;
    }

    /// <summary>
    /// Same-root demotion for unclipped provider rects (A18-2): a candidate point outside
    /// <c>target ∩ nearest-scroll-viewport</c> must not become <c>InterceptedBy</c> on a same-window
    /// neighbour. Corroboration applies it to the same-root arm alone; a cross-window occluder two
    /// opinions agree on is real evidence.
    /// </summary>
    internal static bool ShouldDemoteOutsideViewport(Point point, Rect targetBounds, Rect? viewport)
    {
        if (viewport is not Rect view)
        {
            return false;
        }

        if (IntersectRects(targetBounds, view) is Rect visible)
        {
            return !PointInRect(point, visible);
        }

        return true;
    }

    internal static int AncestryLimit() => WalkBudget.DefaultMaxRawDepth;

    /// <summary>A rectangle with zero width or zero height has no area to hit-test inside.</summary>
    private static bool RectHasArea(Rect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width)
            && double.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0;

    /// <summary>
    /// Records a node in the cycle guard. A runtime key is compared by key; a node with no key is
    /// compared element by element. Returns false when the node was already seen.
    /// </summary>
    internal static bool RememberAncestorKey<TNode>(
        List<NodeKey> visitedKeys,
        List<TNode> visitedUnkeyed,
        NodeKey key,
        TNode current,
        Func<TNode, TNode, bool> sameElement)
        where TNode : class
    {
        if (key is NodeKey.Runtime)
        {
            if (visitedKeys.Contains(key))
            {
                return false;
            }

            visitedKeys.Add(key);
            return true;
        }

        if (visitedUnkeyed.Any(seen => sameElement(seen, current)))
        {
            return false;
        }

        visitedUnkeyed.Add(current);
        return true;
    }

    internal static Ancestry AncestryWith<TNode>(TNode start, TNode expected, int limit, AncestryWalk<TNode> walk)
        where TNode : class
    {
        var current = start;
        var visitedKeys = new List<NodeKey>();
        var visitedUnkeyed = new List<TNode>();
        for (var step = 0; step < limit; step++)
        {
            if (walk.Deadline.IsExpired())
            {
                return Ancestry.Incomplete;
            }

            if (!RememberAncestorKey(visitedKeys, visitedUnkeyed, walk.Identity(current), current, walk.SameElement))
            {
                return Ancestry.Incomplete;
            }

            var (succeeded, parent) = walk.ParentOf(current);
            if (!succeeded)
            {
                return Ancestry.Incomplete;
            }

            if (parent is null)
            {
                return Ancestry.Absent;
            }

            if (walk.SameElement(parent, expected))
            {
                return Ancestry.Found;
            }

            current = parent;
        }

        return Ancestry.Incomplete;
    }

    /// <summary>
    /// Classifies a hit element against the target. Returns null when the ancestry relation is
    /// unproven in either direction, so the caller reports <c>Unknown</c> rather than an occluder.
    /// </summary>
    internal static HitClassification? ClassifyHitWith<TNode>(TNode target, TNode hit, AncestryWalk<TNode> walk)
        where TNode : class
    {
        var limit = AncestryLimit();
        var reachesTarget = walk.SameElement(target, hit)
            ? Ancestry.Found
            : AncestryWith(hit, target, limit, walk);
        if (reachesTarget == Ancestry.Incomplete)
        {
            return null;
        }

        var isAncestorOfTarget = reachesTarget == Ancestry.Found
            ? Ancestry.Absent
            : AncestryWith(target, hit, limit, walk);
        if (isAncestorOfTarget == Ancestry.Incomplete)
        {
            return null;
        }

        return ClassifyRelation(reachesTarget == Ancestry.Found, isAncestorOfTarget == Ancestry.Found);
    }
}
