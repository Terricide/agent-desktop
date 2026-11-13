using System.Runtime.CompilerServices;
using System.Threading;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input.Clipboard;

/// <summary>
/// Tracks whether a clipboard read this process started is still inside a Win32 call.
///
/// A read that services a delay-rendered format blocks inside <c>GetClipboardData</c> for as long as the
/// owning application takes. When the caller's deadline expires, the caller is told the read timed out, but
/// the worker is still parked in the OS call, so its session's disposal cannot run and this process holds the
/// clipboard open for the rest of the render. A thread blocked in a Win32 call cannot be cancelled, so the
/// answer is not to stop it: it is to refuse the next clipboard operation instead of letting it contend with a
/// worker nothing can reclaim, and to say so.
///
/// The reach of that refusal is exactly one process. An abandoned worker dies with the process that spawned it
/// and Windows releases the clipboard on exit, so a retry from a fresh invocation neither needs this guard nor
/// is affected by it. What it governs is a second clipboard operation inside one invocation, such as a batch
/// entry or a command that reads twice.
/// </summary>
internal static class ClipboardWorkerState
{
    private static readonly StrongBox<int> ProcessCounter = new(0);

    /// <summary>
    /// Held by a clipboard worker for as long as it is running. A worker that returns, normally or by
    /// unwinding, disposes its ticket; one parked inside a Win32 call never does, which is precisely the
    /// condition being detected.
    ///
    /// The counter is a parameter rather than a hard-coded process-wide value so a test can arm one of its
    /// own. Arming the process-wide counter from a test would refuse the live clipboard tests running beside
    /// it, which is a race the guard would have caused rather than caught.
    /// </summary>
    internal sealed class WorkerTicket : IDisposable
    {
        private readonly StrongBox<int> _counter;
        private int _released;

        private WorkerTicket(StrongBox<int> counter)
        {
            _counter = counter;
        }

        internal static WorkerTicket Arm() => ArmOn(ProcessCounter);

        internal static WorkerTicket ArmOn(StrongBox<int> counter)
        {
            Interlocked.Increment(ref counter.Value);
            return new WorkerTicket(counter);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref _counter.Value);
            }
        }
    }

    internal static int Outstanding() => Volatile.Read(ref ProcessCounter.Value);

    /// <summary>
    /// The refusal a given outstanding count implies, separated from where the count is read so it can be
    /// exercised without touching process-wide state.
    /// </summary>
    internal static AdapterError? RefusalFor(int outstanding)
    {
        if (outstanding == 0)
        {
            return null;
        }

        return new AdapterError(
                ErrorCode.AppUnresponsive,
                "A previous clipboard read is still inside GetClipboardData and holds the clipboard open")
            .WithSuggestion(
                "Wait for the previous read's owner to answer, or retry from a new invocation - an abandoned read ends with the process that started it")
            .WithPlatformDetail($"{outstanding} clipboard worker(s) outstanding in this process")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }

    /// <summary>
    /// Refuses when a previous read's worker is still holding the clipboard open.
    ///
    /// <c>EnsureOwnerResponsive</c> answers a different question, whether the owning application is
    /// dispatching messages at all, and a responsive-but-slow renderer passes it, which is how the outstanding
    /// worker came to exist.
    /// </summary>
    internal static void EnsureNoOutstandingWorker()
    {
        AdapterError? error = RefusalFor(Outstanding());
        if (error is not null)
        {
            throw error;
        }
    }
}
