namespace AgentDesktop.Windows.System.Shell;

/// <summary>
/// The dismiss half of <see cref="SurfaceRaise"/>. The Action Center closes on its Win+A toggle and the Start overlay
/// closes on Escape: the Meta toggle re-raises the overlay rather than dismissing it (A26-9). The always-up tray
/// family has nothing to dismiss.
/// <see cref="RaiseThenEscape"/> is Escape with its precondition made explicit. A synthesized Escape goes to whichever
/// window owns the foreground, so it dismisses a surface only while that surface owns it. The overflow flyout is
/// re-raised first, which closes a presented flyout outright and activates a visible one so the Escape reaches it.
/// </summary>
internal enum SurfaceDismiss
{
    None,
    Escape,
    RaiseThenEscape,
    Toggle,
}
