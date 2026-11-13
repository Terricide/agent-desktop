using System.Text.Json;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Storage;

/// <summary>Refmaps live at snapshots/&lt;namespace&gt;/&lt;snapshot_id&gt;/refmap.json. Each is capped at
/// 1 MB, and the store keeps the newest 128 snapshots per namespace, pruning to 96 when it
/// exceeds the cap.</summary>
public sealed class SnapshotStore
{
    public const int MaxRefMapBytes = 1024 * 1024;

    public const int KeepNewest = 128;

    public const int PruneTo = 96;

    public SnapshotStore(StateRoot root)
    {
        _root = root;
    }

    private readonly StateRoot _root;

    public void Save(RefMap map, string namespaceName)
    {
        var text = JsonSerializer.Serialize(
            new RefMapDocument { SnapshotId = map.SnapshotId, Entries = map.Entries },
            RefMapJsonContext.Default.RefMapDocument);
        if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxRefMapBytes)
        {
            throw new AppError("RefMap exceeds the 1 MB cap for one snapshot");
        }

        var directory = _root.SnapshotDirectory(namespaceName, map.SnapshotId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(System.IO.Path.Combine(directory, "refmap.json"), text);
        Prune(namespaceName);
    }

    public RefMap? Load(string snapshotId, string namespaceName)
    {
        var file = System.IO.Path.Combine(_root.SnapshotDirectory(namespaceName, snapshotId), "refmap.json");
        if (!File.Exists(file))
        {
            return null;
        }

        var document = JsonSerializer.Deserialize(File.ReadAllText(file), RefMapJsonContext.Default.RefMapDocument);
        return document is null ? null : RefMap.FromDocument(document);
    }

    private void Prune(string namespaceName)
    {
        var namespaceRoot = System.IO.Path.Combine(_root.Snapshots, namespaceName);
        if (!Directory.Exists(namespaceRoot))
        {
            return;
        }

        var directories = Directory.GetDirectories(namespaceRoot);
        if (directories.Length <= KeepNewest)
        {
            return;
        }

        Array.Sort(directories, (a, b) => Directory.GetLastWriteTime(a).CompareTo(Directory.GetLastWriteTime(b)));
        foreach (var directory in directories.Take(directories.Length - PruneTo))
        {
            Directory.Delete(directory, true);
        }
    }
}
