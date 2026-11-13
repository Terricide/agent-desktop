namespace AgentDesktop.Windows.System.Window;

/// <summary>
/// The window show verbs and the rule that decides whether the placement a window
/// reports satisfies the verb that was asked.
/// </summary>
internal static class WindowOpVerb
{
    internal enum ShowVerb
    {
        Minimize,
        Maximize,
        Restore,
    }

    private const uint ShowMinimized = 2;
    private const uint ShowMaximized = 3;

    /// <summary>
    /// Whether the placement the window reports satisfies the verb that was asked.
    /// </summary>
    /// <remarks>
    /// Restore is "no longer minimized" rather than an exact normal placement: a
    /// window minimized while maximized carries <c>WPF_RESTORETOMAXIMIZED</c>, so
    /// <c>SW_RESTORE</c> correctly returns it to maximized and demanding
    /// <c>SW_SHOWNORMAL</c> would call that ordinary sequence a failure. The product
    /// verb undoes the minimize; it does not promise to un-maximize.
    /// </remarks>
    internal static bool VerbIsSatisfied(ShowVerb verb, uint showCmd) => verb switch
    {
        ShowVerb.Minimize => showCmd == ShowMinimized,
        ShowVerb.Maximize => showCmd == ShowMaximized,
        ShowVerb.Restore => showCmd != ShowMinimized,
        _ => false,
    };
}
