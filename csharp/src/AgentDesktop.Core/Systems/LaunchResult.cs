using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Systems;

/// <summary>What a launch can honestly report. The process starting and the application presenting a window are
/// separate outcomes: a background application never shows one, and a document-based application creates its first
/// window only once it is brought forward. Reporting them separately lets a caller wait for the window it actually
/// asked for instead of a deadline. Run list-apps for the presentation of an application that reports no window.</summary>
public sealed record LaunchResult
{
    public string App { get; init; } = "";

    public ProcessId Pid { get; init; }

    public string? ProcessInstance { get; init; }

    public WindowInfo? Window { get; init; }

    /// <summary>The verified DevTools endpoint, present only when a CDP port was requested and the launched process
    /// answered /json/version.</summary>
    public CdpEndpoint? Cdp { get; init; }

    /// <summary>The application's renderer, detected best-effort from its bundle. Chromium means Electron or CEF; null
    /// means undetected, not "not Chromium". Detection failure never fails the launch itself.</summary>
    public RendererKind? Renderer { get; init; }

    /// <summary>Guidance for the calling agent on how to drive this application, set from the renderer and whether a
    /// CDP endpoint is present. Null when there is nothing more useful to say than the other fields do.</summary>
    public string? Suggestion { get; init; }
}
