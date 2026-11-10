namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// A slot in the ceiling on parked flush threads, returned when the thread finishes.
/// </summary>
/// <remarks>
/// The flush runs on a thread this renderer can abandon, so the ceiling is the whole
/// contract: a client that never reads makes every flush one more abandoned thread, and the
/// process inherits their stack commitments. A full ceiling means the next flush is skipped,
/// so a process that is never read degrades to a silent renderer rather than one whose
/// threads grow without bound. The count is static because one process is one renderer.
/// </remarks>
internal sealed class ParkedFlush : IDisposable
{
    /// <summary>How many flush threads a renderer may leave parked.</summary>
    private const int MaxParkedFlushes = 4;

    private static int open;

    private ParkedFlush() { }

    /// <summary>Takes one slot, or nothing when every flush thread is already parked.</summary>
    internal static ParkedFlush? Claim()
    {
        int held = Interlocked.Increment(ref open);
        if (held > MaxParkedFlushes)
        {
            Interlocked.Decrement(ref open);
            return null;
        }

        return new ParkedFlush();
    }

    /// <summary>
    /// Returns the slot. The flush thread disposes it when the flush completes; a flush that
    /// never completes never returns its slot, which is what the ceiling bounds.
    /// </summary>
    public void Dispose()
    {
        Interlocked.Decrement(ref open);
    }
}
