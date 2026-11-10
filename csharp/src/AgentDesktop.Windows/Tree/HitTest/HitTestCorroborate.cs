using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree.HitTest;

/// <summary>
/// Window-attribution corroboration and occluder evidence for hit testing.
/// <c>InterceptedBy</c> requires two-opinion agreement: the UIA hit element's root and
/// <c>WindowFromPoint</c> then <c>GA_ROOT</c> must agree before an unrelated hit is named.
/// Same-root overlays are named on UIA's verdict alone when both roots equal the target's.
/// An unclipped provider rect demotes the same-root arm only; a cross-window occluder that both
/// opinions agree on is evidence wherever the point falls.
/// Both root walks and the evidence batch are cross-process, so each consults the operation
/// deadline; an expired budget answers <c>Unknown</c> before the attribution decision.
/// </summary>
internal static class HitTestCorroborate
{
    /// <summary>
    /// Properties the occluder evidence batch must read together. <c>IsPassword</c> gates
    /// value-bearing text in <c>ElementProperties.FromReads</c>; omitting it would leave the
    /// secure-field withhold open.
    /// </summary>
    internal static readonly TreeProperty[] OccluderEvidenceProperties =
    {
        TreeProperty.IsPassword,
        TreeProperty.Name,
        TreeProperty.HelpText,
        TreeProperty.FullDescription,
        TreeProperty.ControlType,
        TreeProperty.BoundingRectangle,
    };

    /// <summary>
    /// Which window the two opinions agree owns the point.
    /// </summary>
    internal enum Attribution
    {
        SameRoot,
        CrossWindow,
        Contradicted,
    }

    /// <summary>
    /// One window as a single opinion reports it: the root handle that opinion resolved and the
    /// process owning it. The two travel together because they describe the same window.
    /// </summary>
    internal readonly record struct WindowOpinion(nint? Root, uint? Pid);

    /// <summary>
    /// What one corroboration decision reads. The target's root arrives already resolved, because
    /// the pre-probe iconic guard asks the same question of the same walk.
    /// </summary>
    internal sealed record InterceptionContext(
        Element Target,
        nint? TargetRoot,
        Element Hit,
        Point Point,
        UiaTreeSource Walker,
        Deadline Deadline);

    /// <summary>
    /// Native point handed to <c>WindowFromPoint</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    internal static Attribution InterceptionAttribution(
        WindowOpinion target,
        WindowOpinion hit,
        WindowOpinion win32)
    {
        if (Nonzero(target.Root) is not nint targetRoot)
        {
            return Attribution.Contradicted;
        }
        if (Nonzero(win32.Root) is not nint win32Root)
        {
            return Attribution.Contradicted;
        }
        if (Nonzero(hit.Root) is nint hitRoot)
        {
            if (hitRoot == targetRoot && win32Root == targetRoot)
            {
                return Attribution.SameRoot;
            }
            if (hitRoot != targetRoot && win32Root != targetRoot && win32Root == hitRoot)
            {
                return Attribution.CrossWindow;
            }
            return Attribution.Contradicted;
        }
        return PidWidened(targetRoot, win32Root, target.Pid, hit.Pid, win32.Pid);
    }

    /// <summary>
    /// A pid difference proves two windows belong to different processes; pid equality never
    /// proves the same window, so equality widens nothing.
    /// </summary>
    internal static Attribution PidWidened(
        nint target,
        nint win32,
        uint? targetPid,
        uint? hitPid,
        uint? win32OwnerPid)
    {
        bool widens = targetPid is uint tPid
            && hitPid is uint hPid
            && win32OwnerPid is uint wPid
            && win32 != target
            && hPid != tPid
            && hPid == wPid;
        return widens ? Attribution.CrossWindow : Attribution.Contradicted;
    }

    /// <summary>
    /// The reportable outcome for an attribution. Only the same-root arm answers to the viewport
    /// demotion, and unassembled evidence is <c>Unknown</c>.
    /// </summary>
    internal static HitTestResult InterceptionOutcome(
        Attribution attribution,
        bool demoteForViewport,
        Func<HitTestResult?> evidence)
    {
        switch (attribution)
        {
            case Attribution.Contradicted:
                return HitTestResult.Unknown;
            case Attribution.SameRoot when demoteForViewport:
                return HitTestResult.Unknown;
            default:
                return evidence() ?? HitTestResult.Unknown;
        }
    }

    internal static nint? Nonzero(nint? handle) =>
        handle is nint value && value != 0 ? value : null;

    /// <summary>
    /// Walks to the first non-zero native window handle, then <c>GA_ROOT</c>.
    /// Returns false when the walk could not be completed.
    /// </summary>
    internal static bool TryElementRootHwnd(
        Element start,
        UiaTreeSource walker,
        Deadline deadline,
        out nint? root)
    {
        if (!TryFirstNativeHwnd(start, walker, deadline, out nint? handle))
        {
            root = null;
            return false;
        }
        root = handle is nint hwnd ? RootOfHwnd(hwnd) : null;
        return true;
    }

    /// <remarks>
    /// SEAM: HitTestCorroborate.cs:TryFirstNativeHwnd needs `Element.Clone`,
    /// `Element.TryGetNativeWindowHandle` and `Walk.TryParentStep`. `Element` copies nothing and
    /// reads no window handle, and `Walk` is a generic walk over a source, not a parent-step
    /// helper, so the climb cannot start, cannot test a handle, and cannot step to a parent. Until
    /// those members are ported, this path refuses by name.
    /// </remarks>
    internal static bool TryFirstNativeHwnd(
        Element start,
        UiaTreeSource walker,
        Deadline deadline,
        out nint? handle)
    {
        throw AdapterError.NotSupported("climb from an element to its first native window handle");
    }

    internal static nint? RootOfHwnd(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }
        nint root = GetAncestor(hwnd, GaRoot);
        return root == 0 ? null : root;
    }

    /// <summary>Clamps a coordinate to the native range a point query accepts. A non-finite
    /// coordinate is 0, not a saturation, because a NaN has no side to saturate toward.</summary>
    private static int SaturateCoord(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return value <= int.MinValue ? int.MinValue : (int)value;
    }

    internal static nint? WindowFromPointRoot(Point point)
    {
        NativePoint physical = new()
        {
            X = SaturateCoord(point.X),
            Y = SaturateCoord(point.Y),
        };
        nint hwnd = WindowFromPoint(physical);
        return hwnd == 0 ? null : RootOfHwnd(hwnd);
    }

    internal static uint? PidOfHwnd(nint hwnd)
    {
        if (hwnd == 0)
        {
            return null;
        }

        return WindowIdentity.LiveWindowOwner(hwnd)?.Value;
    }

    internal static uint? ProcessIdOf(Element element) =>
        element.CurrentProcessId() is int pid && pid != 0 ? (uint)pid : null;

    /// <summary>
    /// Assembles occluder evidence from the hit element. Returns null when a required evidence
    /// read failed (bounds unread), so the caller demotes to <c>Unknown</c>. A spent budget skips
    /// the label relation as <c>Failed</c>, never <c>Unlabelled</c>, since nothing was asked.
    /// </summary>
    internal static HitTestResult? OccluderEvidence(Element hit, Deadline deadline)
    {
        ElementProperties properties = ReadOccluderProperties(hit, deadline);
        LabelOutcome label = deadline.IsExpired()
            ? LabelOutcome.Failed
            : NameEvidenceFactory.ReadLabel(hit, false);        return OccluderFromProperties(properties, label);
    }

    /// <summary>
    /// Builds <c>InterceptedBy</c> from a prepared property set.
    /// </summary>
    internal static HitTestResult? OccluderFromProperties(
        ElementProperties properties,
        LabelOutcome label)
    {
        string role = OccluderRole(properties);
        string? name = NameFields.Resolve(properties, label).Name.Value;
        LocatorField<Rect> bounds = properties.Get(TreeProperty.BoundingRectangle).Bounds();
        if (bounds.IsUnknown())
        {
            return null;
        }

        return HitTestResult.InterceptedBy(role, name, bounds.IsKnown() ? bounds.Value : null);
    }

    /// <summary>
    /// An expired budget truncates the batch: the unread properties classify <c>Unknown</c> and
    /// the interception is not named.
    /// </summary>
    internal static ElementProperties ReadOccluderProperties(Element hit, Deadline deadline)
    {
        List<(TreeProperty, PropertyOutcome)> reads = new();
        foreach (TreeProperty property in OccluderEvidenceProperties)
        {
            PropertyOutcome outcome = deadline.IsExpired()
                ? PropertyOutcome.Unknown
                : PropertyUia.ReadOne(hit, property);
            reads.Add((property, outcome));
        }
        return ElementProperties.FromReads(reads);
    }

    internal static string OccluderRole(ElementProperties properties) => Roles.Resolve(properties);

    /// <summary>
    /// The element pids are read only when the hit element's root is unobtainable, which is the
    /// sole arm that consults them. Each <c>ProcessId</c> read is a cross-process call.
    /// </summary>
    internal static HitTestResult CorroborateInterception(
        InterceptionContext context,
        bool demoteForViewport)
    {
        if (!TryElementRootHwnd(context.Hit, context.Walker, context.Deadline, out nint? hitRoot))
        {
            return HitTestClassify.ResultForIncompleteWalk();
        }
        nint? win32Root = WindowFromPointRoot(context.Point);
        uint? win32OwnerPid = win32Root is nint w32 ? PidOfHwnd(w32) : null;
        if (context.Deadline.IsExpired())
        {
            return HitTestResult.Unknown;
        }
        bool widens = Nonzero(hitRoot) is null;
        Attribution attribution = InterceptionAttribution(
            new WindowOpinion(context.TargetRoot, widens ? ProcessIdOf(context.Target) : null),
            new WindowOpinion(hitRoot, widens ? ProcessIdOf(context.Hit) : null),
            new WindowOpinion(win32Root, win32OwnerPid));
        return InterceptionOutcome(
            attribution,
            demoteForViewport,
            () => OccluderEvidence(context.Hit, context.Deadline));
    }

    private const uint GaRoot = 2;

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(NativePoint point);
}
