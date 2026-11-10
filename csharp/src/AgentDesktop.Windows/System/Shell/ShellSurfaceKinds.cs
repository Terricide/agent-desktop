using AgentDesktop.Core.Snapshot;
using static AgentDesktop.Windows.System.Shell.SurfaceDismiss;

namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// The kind table behind open-system-surface. <c>start-menu</c> resolves to the full-screen search-hosted overlay the
/// Meta accelerator raises (A26-9), identified by its <c>SearchTextBox</c> landmark. <c>quick-settings</c> is a
/// build-conditional refusal row: on this build the quick actions are a pane inside the Action Center, so the refusal
/// names <c>action-center</c> as the surface carrying that capability.
/// </summary>
internal static class ShellSurfaceKinds
{
    /// <summary>
    /// The window class every immersive shell surface presents on this build. Matching keys on the class plus the
    /// hosting shell process, never on the element's name, which is localized on this host (A26-1).
    /// </summary>
    internal const string CoreWindowClass = "Windows.UI.Core.CoreWindow";

    internal const string ShellTrayWndClass = "Shell_TrayWnd";
    internal const string TrayNotifyClass = "TrayNotifyWnd";
    internal const string ToolbarClass = "ToolbarWindow32";

    /// <summary>
    /// The notification area's toolbar is nested one level below <c>TrayNotifyWnd</c> rather than being its direct
    /// child. The direct child of that class is a zero-extent sibling whose automation element carries no children, so
    /// a chain that stops one hop short resolves a window that reads empty (A26-5, A26-6).
    /// </summary>
    internal const string SysPagerClass = "SysPager";
    internal const string OverflowWindowClass = "NotifyIconOverflowWindow";

    internal const ushort VkA = 0x41;
    internal const ushort VkLWin = 0x5B;
    internal const ushort VkEscape = 0x1B;

    /// <summary>
    /// The notification list's landmark <c>AutomationId</c> (A26-3), the root the notification reader walks. A center
    /// carrying at least one notification presents it.
    /// </summary>
    internal const string MainListView = "MainListView";

    /// <summary>
    /// The landmarks the empty-center state presents in place of <see cref="MainListView"/> (A26-3). An open center
    /// carrying either is legitimately empty, not a tree this adapter fails to recognize.
    /// </summary>
    internal static readonly IReadOnlyList<string> EmptyCenterLandmarks =
        ["NoNotificationsTextBlock", "ScrollWrapper"];

    /// <summary>
    /// The Action Center's measured landmark <c>AutomationId</c>s, read in both of its content states:
    /// <see cref="MainListView"/> when notifications are present (A26-3), and the empty-center shape's
    /// <c>NoNotificationsTextBlock</c> and <c>ScrollWrapper</c> when none are. One landmark list covers both states so
    /// an empty center still resolves to itself rather than to another <c>ShellExperienceHost</c>-hosted surface.
    /// </summary>
    internal static readonly IReadOnlyList<string> ActionCenterLandmarks =
        [MainListView, EmptyCenterLandmarks[0], EmptyCenterLandmarks[1]];

    internal static readonly IReadOnlyList<string> ActionCenterHosts = ["shellexperiencehost"];

    internal static readonly IReadOnlyList<string> StartHosts =
        ["shellexperiencehost", "searchui", "searchhost", "searchapp", "startmenuexperiencehost"];

    /// <summary>
    /// The search surface's measured landmark: the Start accelerator's overlay carries <c>SearchTextBox</c> at its root
    /// (A26-9). It is also what keeps this kind from resolving the Action Center's CoreWindow, since on this build both
    /// are hosted by the same shell host process and only the landmark tells them apart.
    /// </summary>
    internal static readonly IReadOnlyList<string> StartLandmarks = ["SearchTextBox"];

    private static readonly IReadOnlyList<string> TaskbarChain = [ShellTrayWndClass];

    private static readonly IReadOnlyList<string> TrayFamilyChain =
        [ShellTrayWndClass, TrayNotifyClass, SysPagerClass, ToolbarClass];

    private static readonly IReadOnlyList<string> OverflowChain = [OverflowWindowClass, ToolbarClass];

    private static readonly IReadOnlyList<string> NoHosts = [];

    private static readonly IReadOnlyList<string> NoLandmarks = [];

    private static readonly IReadOnlyList<ushort> NoModifiers = [];

    private static readonly IReadOnlyList<ushort> LWinModifier = [VkLWin];

    private static readonly IReadOnlyList<SurfaceKindRow> Kinds =
    [
        new(
            SnapshotSurface.Taskbar,
            new SurfaceFamily.Win32Class(TaskbarChain),
            new SurfaceRaise.AlreadyRaised(),
            None,
            ExistsOnBuild: true,
            CapabilityHolder: null),
        new(
            SnapshotSurface.SystemTray,
            new SurfaceFamily.Win32Class(TrayFamilyChain),
            new SurfaceRaise.AlreadyRaised(),
            None,
            ExistsOnBuild: true,
            CapabilityHolder: null),
        new(
            SnapshotSurface.SystemTrayOverflow,
            new SurfaceFamily.Win32Class(OverflowChain),
            new SurfaceRaise.ChevronInvoke(),
            RaiseThenEscape,
            ExistsOnBuild: true,
            CapabilityHolder: null),
        new(
            SnapshotSurface.StartMenu,
            new SurfaceFamily.Immersive(CoreWindowClass, StartHosts, StartLandmarks),
            new SurfaceRaise.Accelerator(NoModifiers, VkLWin),
            Escape,
            ExistsOnBuild: true,
            CapabilityHolder: null),
        new(
            SnapshotSurface.ActionCenter,
            new SurfaceFamily.Immersive(CoreWindowClass, ActionCenterHosts, ActionCenterLandmarks),
            new SurfaceRaise.Accelerator(LWinModifier, VkA),
            Toggle,
            ExistsOnBuild: true,
            CapabilityHolder: null),
        new(
            SnapshotSurface.QuickSettings,
            new SurfaceFamily.Immersive(CoreWindowClass, NoHosts, NoLandmarks),
            new SurfaceRaise.AlreadyRaised(),
            None,
            ExistsOnBuild: false,
            CapabilityHolder: "action-center"),
    ];

    /// <summary>
    /// The kind table row for a surface, or null when the surface is not a shell surface this adapter resolves.
    /// </summary>
    internal static SurfaceKindRow? RowFor(SnapshotSurface kind)
    {
        foreach (SurfaceKindRow row in Kinds)
        {
            if (row.Kind == kind)
            {
                return row;
            }
        }

        return null;
    }
}
