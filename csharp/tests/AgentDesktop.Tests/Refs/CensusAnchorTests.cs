using System.IO;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using Xunit;
using AgentDesktop.Tests.Snapshot;

namespace AgentDesktop.Tests.Refs;

/// <summary>A depth-clamped window read: the walk counted children on three groups and never
/// walked them. Each group carrying identity evidence is the boundary the next read resolves
/// through, so each earns a ref. A group with no evidence keeps its parent on the frame instead.
/// The shallow clamps are where this rule is hardest: at depth 1 the whole window is four
/// boundaries, and a boundary that is missed shifts every ref after it.</summary>
public sealed class CensusAnchorTests
{
    private static AccessibilityNode Group(string? name, uint? childrenCount) =>
        new()
        {
            Role = "group",
            ChildrenCount = childrenCount,
            Presentation = new NodePresentation { Bounds = new Rect(0, 0, 100, 20) },
            Identity = name is null
                ? new NodeIdentity()
                : new NodeIdentity { Name = AgentDesktop.Core.Locator.LocatorField<string>.Known(name) },
        };

    [Fact]
    public void AClampedBoundaryWithAStableNameEarnsARef()
    {
        var root = new AccessibilityNode
        {
            Role = "window",
            Identity = new NodeIdentity { Name = AgentDesktop.Core.Locator.LocatorField<string>.Known("Downloads - File Explorer") },
            Children = [Group(null, null), Group("Downloads", 1), Group(null, 1), Group(null, 4)],
        };

        var app = new AppInfo { Name = "Explorer", Pid = ProcessId.From(42) };
        var window = new WindowInfo { Id = "w1", Title = "Downloads - File Explorer", App = "Explorer", Pid = app.Pid };
        var adapter = new FakeAdapter(app, window, root);
        var state = new StateRoot(Path.Combine(Path.GetTempPath(), "ad-census-" + Guid.NewGuid().ToString("N")));
        var captured = new SnapshotCommand(adapter, new SnapshotStore(state), new SessionStore(state))
            .Run("Explorer", null, TreeOptions.Default(), 100);

        var refs = JsonNode.Parse(captured.ToJsonString())!["data"]!["tree"]!;
        var byPath = new Dictionary<string, string?>();
        Walk(refs, "");
        void Walk(JsonNode node, string path)
        {
            byPath[path] = node["ref_id"]?.GetValue<string>()?.Split(":").Last();
            var children = node["children"]?.AsArray();
            if (children is null)
            {
                return;
            }

            for (var i = 0; i < children.Count; i++)
            {
                Walk(children[i]!, path + "." + i);
            }
        }

        Assert.Equal("e1", byPath[".1"]);
    }
}
