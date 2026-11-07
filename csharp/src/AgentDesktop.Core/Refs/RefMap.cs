namespace AgentDesktop.Core.Refs;

/// <summary>Maps refs to entries for one snapshot. Refs are allocated in the order the
/// capture observed them, which is depth-first, so a ref names position in the tree.</summary>
public sealed class RefMap
{
    private readonly List<RefEntry> _entries = new();

    public RefMap(string snapshotId)
    {
        SnapshotId = snapshotId;
    }

    public string SnapshotId { get; }

    public int Count => _entries.Count;

    /// <summary>Allocates the next ref and returns it. The ref is the map key, not entry
    /// state, so an entry carries no ref of its own.</summary>
    public string Add(RefProcess process, RefEntryIdentity identity, RefGeometry geometry)
    {
        var reference = "e" + (_entries.Count + 1);
        _entries.Add(new RefEntry { Process = process, Identity = identity, Geometry = geometry });
        return reference;
    }

    /// <summary>Only the snapshot-qualified form is accepted. A bare ref is not a key.</summary>
    public RefEntry? Find(string qualified)
    {
        var parts = qualified.Split(':');
        if (parts.Length != 2 || parts[0] != "@" + SnapshotId)
        {
            return null;
        }
        if (!parts[1].StartsWith('e') || !int.TryParse(parts[1].AsSpan(1), out var ordinal) || ordinal < 1)
        {
            return null;
        }
        return ordinal <= _entries.Count ? _entries[ordinal - 1] : null;
    }
}
