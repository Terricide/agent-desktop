using System.Threading;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Capture;

/// <summary>
/// Bounded workers for Win32 calls that cannot be interrupted by a deadline. Each call runs on a dedicated
/// background thread while the caller waits at most the remaining budget. A call that outlives its budget keeps
/// its worker and its owned resources, so the single-flight slot stays held until the worker returns.
/// </summary>
internal static class NativeCall
{
    private static readonly ActiveFlag CaptureActive = new();

    /// <summary>Whether no window capture holds the process-wide single-flight slot.</summary>
    internal static bool CaptureIsIdle() => CaptureActive.IsIdle;

    internal static T Capture<T>(Deadline deadline, string label, Func<T> call) =>
        Run(deadline, CaptureActive, label, DeliverySemantics.NotDelivered(), call);

    private static T Run<T>(
        Deadline deadline,
        ActiveFlag active,
        string label,
        DeliverySemantics timeoutDelivery,
        Func<T> call)
    {
        deadline.EnsureRemaining();
        if (!active.TryAcquire())
        {
            throw new AdapterError(ErrorCode.AppUnresponsive, $"A previous {label} call is still running")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() => RunWorker(active, completion, label, timeoutDelivery, call))
        {
            Name = $"agent-desktop-{label}",
            IsBackground = true,
        };

        try
        {
            worker.Start();
        }
        catch (Exception error) when (error is OutOfMemoryException or ThreadStartException)
        {
            active.Release();
            throw new AdapterError(ErrorCode.Internal, $"Could not start {label} worker: {error.Message}")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        var finished = Task.WaitAny(new Task[] { completion.Task }, TimeSpan.FromMilliseconds(deadline.RemainingMs()));
        if (finished == -1)
        {
            throw deadline.TimeoutError()
                .WithPlatformDetail($"{label} remains in flight; its worker retains owned resources")
                .WithDisposition(timeoutDelivery);
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    private static void RunWorker<T>(
        ActiveFlag active,
        TaskCompletionSource<T> completion,
        string label,
        DeliverySemantics timeoutDelivery,
        Func<T> call)
    {
        try
        {
            completion.TrySetResult(call());
        }
        catch (AdapterError error)
        {
            completion.TrySetException(error);
        }
        catch (Exception)
        {
            completion.TrySetException(
                new AdapterError(ErrorCode.Internal, $"{label} worker disconnected before returning")
                    .WithDisposition(timeoutDelivery));
        }
        finally
        {
            active.Release();
        }
    }

    private sealed class ActiveFlag
    {
        private int _held;

        internal bool IsIdle => Volatile.Read(ref _held) == 0;

        internal bool TryAcquire() => Interlocked.CompareExchange(ref _held, 1, 0) == 0;

        internal void Release() => Volatile.Write(ref _held, 0);
    }
}
