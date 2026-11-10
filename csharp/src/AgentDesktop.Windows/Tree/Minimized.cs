using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
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
        throw AdapterError.NotSupported("owning_window_minimized");
    }

    /// <summary>WinForms reports unclipped descendants as on-screen; this intersects their scroll viewports.</summary>
    internal static bool OutsideScrollViewports(Element element, Deadline deadline)
    {
        var visibleField = element.BoundingRectangle();
        if (!visibleField.IsKnown())
        {
            return false;
        }

        deadline.EnsureRemaining();
        throw AdapterError.NotSupported("outside_scroll_viewports");
    }

    /// <summary>Reports a minimized owner's whole subtree offscreen in the read set.
    /// A minimized top-level window keeps reporting usable rectangles and a clear
    /// <c>IsOffscreen</c> on its descendants (A14-8 measured the descendants false throughout
    /// on the COM stack), so the visibility gate and "is visible" both read the element as
    /// shown. Overwriting the one flag carries the window truth into every projection the
    /// shared read serves, without touching what the walk or the resolver collect on their
    /// own paths.</summary>
    internal static void StampMinimizedOffscreen(ElementProperties properties)
    {
        throw AdapterError.NotSupported("stamp_minimized_offscreen");
    }

    [LibraryImport("user32.dll", EntryPoint = "IsIconic")]
    private static partial int IsIconic(IntPtr hwnd);
}
