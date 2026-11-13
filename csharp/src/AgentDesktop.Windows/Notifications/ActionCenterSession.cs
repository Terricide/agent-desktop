using System.Diagnostics;
using System.Runtime.ExceptionServices;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System.Shell;

namespace AgentDesktop.Windows.Notifications;

/// <summary>
/// Opens and closes the Action Center around exactly one call. Port of
/// <c>crates/windows/src/notifications/session.rs</c>.
/// </summary>
/// <remarks>
/// A center already presented on entry is adopted, never raised, and is left presented on exit; a center this
/// session raised itself is dismissed on exit. The teardown runs on every path out of the wrapped call, so a
/// raised center is never leaked behind a failed read. The session carries no lifecycle beyond its one call:
/// every call that needs the surface opens its own session, so a poll loop opens and closes per poll.
/// </remarks>
internal sealed class ActionCenterSession : IDisposable
{
    private const long CleanupTimeoutMs = 2_000;

    private bool _closePending;

    private bool _cleanupOnDrop;

    private ActionCenterSession(nint hwnd, bool closePending)
    {
        Hwnd = hwnd;
        _closePending = closePending;
        _cleanupOnDrop = true;
    }

    /// <summary>The window handle the wrapped call reads and acts on.</summary>
    internal nint Hwnd { get; }

    /// <summary>
    /// Adopts a presented center, raises one when the policy permits the foreground to move, and refuses the
    /// raise headlessly before anything moves on the desktop.
    /// </summary>
    internal static ActionCenterSession Open(InteractionPolicy policy, Deadline deadline)
    {
        var surface = ShellSurface.ResolveSurface(SnapshotSurface.ActionCenter, deadline);
        if (surface is not null)
        {
            return new ActionCenterSession(HwndOf(surface), false);
        }

        if (policy != InteractionPolicy.Physical)
        {
            throw ClosedCenterPolicyError(policy);
        }

        return Raise(policy, deadline);
    }

    /// <summary>
    /// Runs one call against the session, then closes the session, and returns whichever answer the merge
    /// of the two leaves: a cleanup failure replaces an apparent success, and the operation's own error wins
    /// when the cleanup also failed.
    /// </summary>
    internal static T Close<T>(ActionCenterSession session, Func<T> operation)
    {
        T result = default!;
        AdapterError? operationError = null;
        try
        {
            result = operation();
        }
        catch (AdapterError error)
        {
            operationError = error;
        }

        var cleanupError = session.Cleanup();
        session._cleanupOnDrop = false;

        if (operationError is null && cleanupError is null)
        {
            return result;
        }

        if (operationError is null)
        {
            throw cleanupError!;
        }

        if (cleanupError is not null)
        {
            Trace.WriteLine($"Action Center cleanup also failed after the operation failed: {cleanupError.Message}");
        }

        ExceptionDispatchInfo.Capture(operationError).Throw();
        return result;
    }

    /// <summary>The teardown a session that was never closed explicitly owes, the same cleanup the wrapped
    /// call's exit runs. A failure here is recorded, never thrown: the caller already has its answer.</summary>
    public void Dispose()
    {
        if (!_cleanupOnDrop)
        {
            return;
        }

        var error = Cleanup();
        if (error is not null)
        {
            Trace.WriteLine($"Action Center cleanup failed in Dispose: {error.Message}");
        }
    }

    private static ActionCenterSession Raise(InteractionPolicy policy, Deadline deadline)
    {
        try
        {
            var surface = ShellSurfaceOpen.OpenSurface(SnapshotSurface.ActionCenter, policy, deadline);
            return new ActionCenterSession(HwndOf(surface), true);
        }
        catch (AdapterError)
        {
            var cleanup = CloseCenter();
            if (cleanup is not null)
            {
                Trace.WriteLine($"Action Center cleanup also failed after the operation failed: {cleanup.Message}");
            }

            throw;
        }
    }

    private AdapterError? Cleanup()
    {
        if (!_closePending)
        {
            return null;
        }

        var error = CloseCenter();
        if (error is null)
        {
            _closePending = false;
        }

        return error;
    }

    /// <summary>The dismissal of the center, run on its own recovery budget: cleanup has to be able to
    /// finish after the operation's deadline is spent.</summary>
    private static AdapterError? CloseCenter()
    {
        try
        {
            ShellSurfaceOpen.CloseSurface(SnapshotSurface.ActionCenter, Deadline.DetachedAfter(CleanupTimeoutMs));
            return null;
        }
        catch (AdapterError error)
        {
            return error;
        }
    }

    private static nint HwndOf(WindowInfo surface)
    {
        var number = surface.Id.StartsWith("w-", StringComparison.Ordinal) ? surface.Id[2..] : string.Empty;
        return nint.TryParse(number, out var handle) && handle > 0
            ? handle
            : throw new AdapterError(ErrorCode.InvalidArgs, "Malformed shell surface identifier");
    }

    private static AdapterError ClosedCenterPolicyError(InteractionPolicy policy) =>
        AdapterError.PolicyDeniedForPolicy(
            "The Action Center is closed and observation cannot open it in headless mode",
            policy)
            .WithSuggestion(
                "Open the Action Center yourself or pass --headed to allow opening and restoring desktop focus.");
}
