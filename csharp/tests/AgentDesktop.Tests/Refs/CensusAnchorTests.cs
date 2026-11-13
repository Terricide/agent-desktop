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
///
/// The clamps are where this rule is hardest: at depth 1 the presented window is four boundaries.
/// A shallow walk cannot prove any of them, because it never saw the nodes below its depth, so the
/// capture counts a whole-window read and presents a cut of that one observation. A fixture that
/// hands the adapter a clamped tree proves nothing, and that is the honest outcome, not the one
/// this test is about.</summary>
public sealed class CensusAnchorTests
{
    private static AccessibilityNode Group(string? name, uint? childrenCount, List<AccessibilityNode> children) =>
        new()
        {
            Role = "group",
            ChildrenCount = childrenCount,
            Children = children,
            Presentation = new NodePresentation { Bounds = new Rect(0, 0, 100, 20) },
            Identity = name is null
                ? new NodeIdentity()
                : new NodeIdentity { Name = AgentDesktop.Core.Locator.LocatorField<string>.Known(name) },
        };

    /// <summary>The whole window the capture counts: every group walked to its end, so no
    /// boundary hides a node the census never saw. A group the walk finished records no child
    /// count, which is what makes the read exhaustive.</summary>
    private static AccessibilityNode CompleteRead() =>
        new()
        {
            Role = "window",
            Identity = new NodeIdentity { Name = AgentDesktop.Core.Locator.LocatorField<string>.Known("Downloads - File Explorer") },
            Children =
            [
                Group(null, null, []),
                Group("Downloads", 1, [Group("Today", null, [])]),
                Group(null, 1, [Group("Recent", null, [])]),
                Group(null, 4, [Group("Desktop", null, []), Group("Documents", null, []), Group("Pictures", null, []), Group("Music", null, [])]),
            ],
        };

    /// <summary>The same window cut at depth 1, which is what a shallow walk hands the adapter when
    /// it never sees below its depth.</summary>
    private static AccessibilityNode ClampedRead() =>
        new()
        {
            Role = "window",
            Identity = new NodeIdentity { Name = AgentDesktop.Core.Locator.LocatorField<string>.Known("Downloads - File Explorer") },
            Children = [Group(null, null, []), Group("Downloads", 1, []), Group(null, 1, []), Group(null, 4, [])],
        };

    private static JsonNode Run(AccessibilityNode tree, byte maxDepth)
    {
        var app = new AppInfo { Name = "Explorer", Pid = ProcessId.From(42) };
        var window = new WindowInfo { Id = "w1", Title = "Downloads - File Explorer", App = "Explorer", Pid = app.Pid };
        var adapter = new FakeAdapter(app, window, tree);
        var state = new StateRoot(Path.Combine(Path.GetTempPath(), "ad-census-" + Guid.NewGuid().ToString("N")));
        return JsonNode.Parse(new SnapshotCommand(adapter, new SnapshotStore(state), new SessionStore(state))
            .Run("Explorer", null, TreeOptions.Default() with { MaxDepth = maxDepth }, 100).ToJsonString())!;
    }

    private static Dictionary<string, string?> RefsByPath(JsonNode envelope) =>
        RefsByPathTree(envelope["data"]!["tree"]!);

    private static Dictionary<string, string?> RefsByPathTree(JsonNode tree)
    {
        var byPath = new Dictionary<string, string?>();
        Walk(tree, "");
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
        return byPath;
    }

    [Fact]
    public void AClampedBoundaryWithAStableNameEarnsARef()
    {
        var byPath = RefsByPath(Run(CompleteRead(), 1));

        Assert.Equal("e1", byPath[".1"]);
    }

    [Fact]
    public void AShallowWalkThatNeverSawBelowItsDepthProvesNothing()
    {
        var byPath = RefsByPath(Run(ClampedRead(), 1));

        Assert.Null(byPath[".1"]);
    }
}
