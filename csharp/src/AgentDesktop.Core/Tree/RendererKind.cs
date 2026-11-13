namespace AgentDesktop.Core.Tree;

/// <summary>The renderer an application's window contents are drawn by, detected best-effort from its bundle.
/// Chromium covers both Electron and CEF; no consumer needs to tell them apart, only whether a CDP client can attach.</summary>
public enum RendererKind
{
    Chromium,
}
