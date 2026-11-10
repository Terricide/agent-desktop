using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree.HitTest;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>Answers whether an element's owning top-level window is minimized, and
/// corrects the offscreen state of a minimized window's subtree.</summary>
internal static partial class Minimized
{
    /// <summary>Whether the element's owning top-level window is minimized right now.
    /// Null answers nothing: the budget expired, the client or walker would not build, or the
    /// climb never reached a window. Callers treat null as unknown and change nothing, because
    /// a failed window question must never invent an offscreen state.</summary>
    internal static bool? OwningWindowMinimized(Element element, Deadline deadline)
    {
        deadline.EnsureRemaining();
        if (Walker.ElementRootHwnd(element, deadline) is not nint root || root == 0)
        {
            return null;
        }

        return IsIconic(new IntPtr(root)) != 0;
    }

    /// <summary>WinForms reports unclipped descendants as on-screen; this intersects their scroll
    /// viewports. A viewport that does not contain the element's rectangle means the element is
    /// clipped out of what the user can see.</summary>
    internal static bool OutsideScrollViewports(Element element, Deadline deadline)
    {
        var visibleField = element.BoundingRectangle();
        if (!visibleField.IsKnown())
        {
            return false;
        }

        deadline.EnsureRemaining();
        var viewport = Walker.NearestScrollViewportBounds(element, deadline);
        if (viewport is null)
        {
            return false;
        }

        return HitTestClassify.IntersectRects(visibleField.Value, viewport.Value) is null;
    }

    /// <summary>Reports a minimized owner's whole subtree offscreen in the read set.
    /// A minimized top-level window keeps reporting usable rectangles and a clear
    /// <c>IsOffscreen</c> on its descendants (A14-8 measured the descendants false throughout
    /// on the COM stack), so the visibility gate and "is visible" both read the element as
    /// shown. Overwriting the one flag carries the window truth into every projection the
    /// shared read serves, without touching what the walk or the resolver collect on their
    /// own paths.</summary>
    internal static void StampMinimizedOffscreen(ElementProperties properties) =>
        properties.Set(TreeProperty.IsOffscreen, new PropertyOutcome.Known(new PropertyValue.Flag(true)));

    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    private static partial int IsIconic(IntPtr hwnd);
}
