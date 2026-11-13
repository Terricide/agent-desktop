using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The renderer: one process, one window, one pipe, for as long as its session lasts.
/// Entered before argument parsing, so a process launched as the child never reaches the CLI.
/// It claims its pipe first and creates its window second: the pipe is the singleton lock, and a
/// child that loses that race has to withdraw before anything is drawn, or a duplicate overlay
/// flashes on screen. A child whose window then fails to appear exits too, releasing the name so
/// the next enable can start a replacement rather than connecting forever to a renderer that
/// draws nothing.
/// </summary>
internal static class Child
{
    /// <summary>
    /// Returns true when this process was launched as the overlay's renderer. Checked ahead of
    /// the command line parser, which is why the child never parses a command line. The error is
    /// null when the renderer ran to completion.
    /// </summary>
    internal static bool TryEnterFromEnvironment(out AdapterError? error)
    {
        error = null;
        var marker = global::System.Environment.GetEnvironmentVariable(PipeName.ChildMarker);
        if (marker is null)
        {
            return false;
        }
        if (marker != PipeName.ProtocolGeneration)
        {
            error = AdapterError.Internal(
                "The cursor overlay child was started for a different protocol generation");
            return true;
        }
        var arguments = global::System.Environment.GetCommandLineArgs();
        var parsed = PipeName.ParseChildArguments(arguments);
        if (parsed is null)
        {
            return false;
        }
        var (sessionId, _, agentId) = parsed.Value;
        error = Run(sessionId, agentId);
        return true;
    }

    /// <summary>
    /// Runs the renderer. Returns null on success, or the error the renderer stopped with.
    /// </summary>
    private static AdapterError? Run(string sessionId, string? agentId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return AdapterError.NotSupported("run_cursor_overlay_child");
        }
        return RunOnWindows(sessionId, agentId);
    }

    private static AdapterError? RunOnWindows(string sessionId, string? agentId)
    {
        try
        {
            DpiAwareness.EnsurePerMonitorV2();
            var name = PipeName.Name(AgentDesktopRoot(), sessionId, agentId);
            var listener = ClaimListener(name);
            if (listener is null)
            {
                return null;
            }
            var host = SurfaceHost.Create(sessionId);
            Serve(listener, host, sessionId, agentId);
            return null;
        }
        catch (AdapterError error)
        {
            return error;
        }
    }

    /// <summary>
    /// The agent desktop state directory. A failure to resolve it is an invalid-argument error,
    /// because the caller's environment is what determines it.
    /// </summary>
    private static string AgentDesktopRoot()
    {
        try
        {
            return StateRoot.FromEnvironment(() => global::System.Environment.GetEnvironmentVariable(StateRoot.EnvironmentVariable)).Path;
        }
        catch (global::System.Exception error)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, error.Message);
        }
    }

    /// <summary>
    /// Claims the pipe name. Returns null when another renderer already serves it, which ends
    /// this process without an error.
    /// </summary>
    private static Listener? ClaimListener(string name)
    {
        try
        {
            return Listener.Claim(name);
        }
        catch (ClaimException claim) when (claim.Kind == ClaimErrorKind.AlreadyServed)
        {
            return null;
        }
        catch (ClaimException claim)
        {
            throw claim.Error ?? AdapterError.Internal(claim.Message);
        }
    }

    /// <summary>
    /// Whether this renderer is the one a control is addressed to.
    /// </summary>
    /// <remarks>
    /// An agent's renderer answers only its own agent's controls, or the session-wide ones. A
    /// <c>Disable</c> carries no agent by design - stopping a session stops all of it - so it is
    /// accepted whoever it reaches, and the broadcast that sends it relies on exactly that.
    /// </remarks>
    internal static bool ServesAgent(string? mine, string? theirs, bool sessionWide)
    {
        return sessionWide || mine == theirs;
    }

    /// <summary>
    /// The connection is released before anything that only has to look right is drawn.
    /// </summary>
    /// <remarks>
    /// A control is answered as soon as what its sender waits for is true, and the pipe is
    /// disconnected immediately after, so the next client is already being accepted while the card
    /// is still easing in and the click flourish is still playing. Holding the connection for the
    /// whole animation left a second overlaid action connecting to a busy pipe for the better part
    /// of a second - longer than the budget it had - and reporting that nothing had rendered.
    /// A <c>Disable</c> never settles: the process is about to exit, and there is nothing left to
    /// draw onto.
    /// </remarks>
    private static void Serve(Listener listener, SurfaceHost host, string sessionId, string? agentId)
    {
        while (true)
        {
            switch (listener.NextControl(host.IdleTick()))
            {
                case ControlAccepted accepted:
                    var control = accepted.Control;
                    var ours = control.SessionId == sessionId
                        && ServesAgent(agentId, control.AgentId, control.IsDisable);
                    if (ours)
                    {
                        host.Apply(control);
                        if (Framing.IsAcknowledged(control))
                        {
                            listener.Acknowledge();
                        }
                    }
                    listener.Finish();
                    if (ours && control.IsDisable)
                    {
                        return;
                    }
                    if (ours)
                    {
                        host.Settle(control, () => listener.ControlWaiting());
                    }
                    break;
                case IdleAccepted:
                    host.Rest();
                    if (host.SessionFinished())
                    {
                        return;
                    }
                    break;
                case BrokenAccepted broken:
                    throw broken.Error;
            }
        }
    }
}
