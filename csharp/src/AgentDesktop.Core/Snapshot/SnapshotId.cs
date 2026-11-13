namespace AgentDesktop.Core.Snapshot;

/// <summary>A snapshot id: `s` followed by base-36 digits, the shape the ref token grammar
/// accepts. The seed mixes the clock, the process id and a per-process counter, so ids are
/// unique inside a process and across processes without a filesystem touch.</summary>
public static class SnapshotId
{
    private static long _counter;

    /// <summary>The next id for this process.</summary>
    public static string New()
    {
        var nanos = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds * 1_000_000L;
        return "s" + Base36(Mix(nanos, Environment.ProcessId, Interlocked.Increment(ref _counter)));
    }

    private static ulong Mix(long nanos, int pid, long counter)
    {
        var hash = 14695981267347246415UL;
        foreach (var bytes in new[] { BitConverter.GetBytes(nanos), BitConverter.GetBytes(pid), BitConverter.GetBytes(counter) })
        {
            foreach (var b in bytes)
            {
                hash = (hash ^ b) * 1099511628211UL;
            }
        }

        return hash;
    }

    private static string Base36(ulong value)
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        const int minLength = 4;
        var buffer = new List<char>();
        while (value > 0)
        {
            buffer.Add(digits[(int)(value % 36)]);
            value /= 36;
        }

        while (buffer.Count < minLength)
        {
            buffer.Add('0');
        }

        buffer.Reverse();
        return new string(buffer.ToArray());
    }
}
