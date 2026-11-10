using AgentDesktop.Core.Context;
using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows;

/// <summary>The adapter's per-process Chromium settle state. The adapter is reused across a
/// batch or an FFI session, so one process settling must never suppress another process's
/// activation. The set is keyed on the process identity including its instance token, so a
/// recycled pid never inherits another process's settle state.</summary>
public sealed partial class WindowsAdapter
{
    private readonly object _activationLock = new();

    private readonly HashSet<ProcessIdentity> _rendererActivationAttempted = new();

    /// <summary>Records that the Chromium settle has run for <paramref name="process"/>, scoped
    /// to its generation so a recycled pid never inherits another process's settle state.</summary>
    internal void NoteRendererActivationAttempted(ProcessIdentity process)
    {
        lock (_activationLock)
        {
            _rendererActivationAttempted.Add(process);
        }
    }

    /// <summary>Whether the Chromium settle has already run for <paramref name="process"/>.</summary>
    internal bool RendererActivationAttempted(ProcessIdentity process)
    {
        lock (_activationLock)
        {
            return _rendererActivationAttempted.Contains(process);
        }
    }

    /// <summary>The Chromium settle: connecting to UI Automation already triggered the renderer's
    /// asynchronous accessibility build, so activation is the connection plus this settle. A short
    /// backoff lets the build start landing before core's loop re-walks; core's own retry backoff is
    /// what turns this into a real wait. The settle is noted for this process, so a same-process
    /// retry keeps only the walk in the loop, never a second activation, while an unrelated process
    /// observed through the same adapter still gets its own pass.</summary>
    public void ActivateRendererAccessibility(ProcessIdentity process, InteractionLease lease)
    {
        NoteRendererActivationAttempted(process);
        var remaining = lease.Deadline().Remaining();
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        Thread.Sleep(remaining < MaxSettleSleep ? remaining : MaxSettleSleep);
    }

    private static readonly TimeSpan MaxSettleSleep = TimeSpan.FromMilliseconds(50);
}
