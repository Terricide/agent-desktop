using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.Window;

/// <summary>Polls queued placement with the 80 ms reread delay retained from A21-5.</summary>
internal static class WindowOpWait
{
    internal static void WaitForPlacement(Deadline deadline, global::System.Func<bool> read)
    {
        while (true)
        {
            var pause = RunAfterWrite(() => deadline.RemainingSlice(global::System.TimeSpan.FromMilliseconds(80)));
            global::System.Threading.Thread.Sleep(pause);
            RunAfterWrite(() => { deadline.EnsureRemaining(); return true; });
            if (RunAfterWrite(read))
            {
                return;
            }
        }
    }

    private static T RunAfterWrite<T>(global::System.Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (AdapterError error)
        {
            throw error.WithDisposition(DeliverySemantics.DeliveredUnverified());
        }
    }
}
