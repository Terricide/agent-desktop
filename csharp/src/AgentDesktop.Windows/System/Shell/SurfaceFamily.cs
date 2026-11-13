namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// The two reach families, chosen per surface. The tray family is yielded by the top-level window walk but rejected by
/// the agent-window filter on its tool bit. The immersive family never appears in that walk and is reached only through
/// the UI Automation root's children (A26-1).
/// <see cref="Win32Class"/> names the exact window-class chain a kind roots at, top-level first.
/// </summary>
internal abstract record SurfaceFamily
{
    /// <summary>A chain of window classes, top-level first, that the kind roots at.</summary>
    internal sealed record Win32Class(IReadOnlyList<string> Chain) : SurfaceFamily;

    /// <summary>
    /// An immersive shell surface: its window class, the hosting shell process images, and the landmark
    /// <c>AutomationId</c>s that tell it apart from another surface hosted by the same process.
    /// </summary>
    internal sealed record Immersive(
        string ExpectedClass,
        IReadOnlyList<string> HostImages,
        IReadOnlyList<string> Landmarks) : SurfaceFamily;
}
