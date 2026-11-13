using System.Text.Json;
using System.Text.Json.Serialization;
using AgentDesktop.Core.Refs;

namespace AgentDesktop.Core.Storage;

/// <summary>The stored form of one refmap: the snapshot id and the entries in ref order, so
/// e1 is entries[0]. A source-generated context keeps the persisted shape trim-safe.</summary>
public sealed class RefMapDocument
{
    public string SnapshotId { get; set; } = "";

    public List<RefEntry> Entries { get; set; } = new();
}

[JsonSerializable(typeof(RefMapDocument))]
[JsonSerializable(typeof(RefEntry))]
[JsonSerializable(typeof(List<RefEntry>))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class RefMapJsonContext : JsonSerializerContext
{
}
