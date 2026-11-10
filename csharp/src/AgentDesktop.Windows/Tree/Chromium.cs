using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>Chromium and Electron root detection, renderer-activation gating, and the errors
/// that drive the activation settle. Port of crates/windows/src/tree/chromium.rs.
///
/// The top-level window class Chromium ships (A4-4 observed it on Obsidian's top-level and
/// render-host windows). Detection keys on this class alone; an app allowlist would be the
/// macOS renderer-probe anti-pattern this adapter deliberately refuses.
///
/// The pre-activation shell shape A16-11 censused: a full-depth walk of a not-yet-settled
/// Chromium root lands in the tens of nodes, against a settled tree of 165. A shell under this
/// line is the connection-triggered build's before state, not a genuinely small window.</summary>
internal static class Chromium
{
    internal const string WindowClass = "Chrome_WidgetWin_1";

    internal const int ShellNodeCeiling = 32;

    /// <summary>Whether the root window is Chromium or Electron provenance, read from the root
    /// element's class name: a one-property live read, never a second walk. This gates the
    /// wrapper skip and the activation settle.</summary>
    internal static bool IsChromiumRoot(Element root)
    {
        var className = root.CurrentClassName();
        return className.Kind == LocatorFieldKind.Known && className.Value?.Trim() == WindowClass;
    }

    /// <summary>Whether a full-depth walk of a detected-Chromium root landed on the shell shape.
    /// A depth-clamped observation never claims this: the walk stopped above the web content by
    /// design (the #117 lesson), so its small tree says nothing about the renderer. Only
    /// consulted for a full-depth walk.</summary>
    internal static bool IsShellShaped(WalkStats stats, ObservationRequest request) =>
        ReachedFullDepth(stats.Traversal.MaxLogicalDepth, request) && stats.Traversal.NodesVisited <= ShellNodeCeiling;

    /// <summary>Whether a walk genuinely ran out of tree rather than being depth-clamped. This
    /// mirrors macOS's observation_reached_tree_end (the #117 lesson): a depth-clamped observation
    /// stops above the web content by design, so only a walk that ran to a natural end can
    /// conclude the tree is genuinely thin.</summary>
    internal static bool ReachedFullDepth(byte maxLogicalDepth, ObservationRequest request) =>
        maxLogicalDepth < request.MaxLogicalDepth;

    /// <summary>Whether this observation may trigger renderer activation, the macOS
    /// activation_eligible gating: a window-rooted, window-surface observation of a Chromium
    /// shell. An element-rooted or non-window-surface walk never demands activation, so a
    /// drill-down into a shell-shaped subtree returns its thin tree instead of looping the settle.</summary>
    internal static bool ActivationEligible(ObservationRoot root, ObservationRequest request) =>
        root is ObservationRoot.Window
        && request.Surface == SnapshotSurface.Window;

    /// <summary>The error core's loop turns into a settle retry. The marker is what
    /// requires_renderer_accessibility_activation reads.</summary>
    internal static AdapterError ActivationRequired(WalkStats stats) =>
        new AdapterError(ErrorCode.ActionNotSupported, "The Chromium renderer has not published its accessibility tree yet")
            .WithDetails(new global::System.Text.Json.Nodes.JsonObject
            {
                ["renderer_accessibility_activation_required"] = true,
                ["nodes_observed"] = (long)stats.Traversal.NodesVisited,
            })
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>The platform detail a tree still thin after settle carries. Names Chromium's
    /// --force-renderer-accessibility switch, and recommends raising --timeout-ms when the
    /// observation is cut short before the build completes. No target-derived text enters here.</summary>
    internal static string StillThinDetail() =>
        "The tree is still thin after Chromium's accessibility build settled. "
        + "If this application is Chromium-based, it may require the "
        + "--force-renderer-accessibility switch; if the observation is being cut "
        + "before the build completes, increase snapshot --timeout-ms.";
}
