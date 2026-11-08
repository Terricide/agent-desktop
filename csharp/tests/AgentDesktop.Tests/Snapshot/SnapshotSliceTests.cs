using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using System.IO;
using Xunit;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Tests.Snapshot;

/// <summary>One observation the fake adapter returns, with the window it belongs to.</summary>
public sealed class FakeAdapter : PlatformAdapter
{
    public FakeAdapter(AppInfo app, WindowInfo window, AccessibilityNode tree)
    {
        App = app;
        Window = window;
        Tree = tree;
    }

    public AppInfo App { get; }

    public WindowInfo Window { get; }

    public AccessibilityNode Tree { get; }

    public int ResolveCalls { get; private set; }

    public ActionRequest? LastRequest { get; private set; }

    public List<AppInfo> ListApps(Deadline deadline) => new() { App };

    public List<WindowInfo> ListWindows(WindowFilter filter, Deadline deadline) => new() { Window };

    public ObservedTree ObserveTree(ObservationRoot root, ObservationRequest request) =>
        new(Tree, true, false, 0);

    public NativeHandle ResolveElementStrict(RefEntry entry, Deadline deadline)
    {
        ResolveCalls++;
        return NativeHandle.New(entry.Identity.Role);
    }

    public ActionResult ExecuteAction(NativeHandle handle, ActionRequest request, InteractionLease lease)
    {
        LastRequest = request;
        return ActionResult.DeliveredUnverified(request.Action.Name());
    }
}

public class SnapshotSliceTests
{
    private static (SnapshotCommand, RefActionCommand, StateRoot) Slice(AccessibilityNode tree)
    {
        var app = new AppInfo { Name = "Notes", Pid = ProcessId.From(42) };
        var window = new WindowInfo { Id = "w1", Title = "Notes", App = "Notes", Pid = app.Pid };
        var adapter = new FakeAdapter(app, window, tree);
        var root = new StateRoot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ad-slice-" + Guid.NewGuid().ToString("N")));
        var sessions = new SessionStore(root);
        var snapshots = new SnapshotStore(root);
        return (new SnapshotCommand(adapter, snapshots, sessions), new RefActionCommand(adapter, snapshots, sessions), root);
    }

    private static string SnapshotIdOf(EnvelopeResponse response) =>
        System.Text.Json.Nodes.JsonNode.Parse(response.ToJsonString())!["data"]!["snapshot_id"]!.GetValue<string>();

    private static AccessibilityNode Node(string role, string name, List<AccessibilityNode> children, List<string> actions) =>
        new()
        {
            Role = role,
            Identity = new NodeIdentity { Name = name },
            Presentation = new NodePresentation { AvailableActions = actions, Bounds = new Rect(0, 0, 10, 10) },
            Children = children,
        };

    [Fact]
    public void SnapshotAllocatesRefsDepthFirstAndPersistsTheRefMap()
    {
        var leaf = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Node("group", "Notes", new List<AccessibilityNode> { leaf }, []);
        var (snapshot, _, root) = Slice(tree);

        var response = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var json = response.ToJsonString();
        var persisted = SnapshotIdOf(response);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"ref_count\":1", json);
        Assert.Contains("\"complete\":true", json);
        Assert.Contains("\"ref_id\":\"@" + persisted + ":e1\"", json);
        Assert.True(File.Exists(System.IO.Path.Combine(root.Snapshots, "default", persisted, "refmap.json")));
    }

    [Fact]
    public void ARefActionResolvesFromThePersistedCaptureProofAndReturnsTheReport()
    {
        var leaf = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Node("group", "Notes", new List<AccessibilityNode> { leaf }, []);
        var (snapshot, action, _) = Slice(tree);

        var captured = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var snapshotId = SnapshotIdOf(captured);

        var response = action.Run("click", "@" + snapshotId + ":e1", new Action { Kind = ActionKind.Click }, null);
        var json = response.ToJsonString();

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"action\":\"click\"", json);
        Assert.Contains("\"delivery\":\"delivered_unverified\"", json);
        Assert.Contains("\"retry\":\"unsafe\"", json);
    }

    [Fact]
    public void ABareRefIsRejectedAsInvalidArgsBeforeTheApplicationIsRead()
    {
        var leaf = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Node("group", "Notes", new List<AccessibilityNode> { leaf }, []);
        var (snapshot, action, _) = Slice(tree);
        var captured = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var snapshotId = SnapshotIdOf(captured);

        var response = action.Run("click", "e1", new Action { Kind = ActionKind.Click }, null);
        var json = response.ToJsonString();

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("\"code\":\"INVALID_ARGS\"", json);
    }

    [Fact]
    public void ARefFromAnotherSessionsNamespaceIsNotFound()
    {
        var leaf = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Node("group", "Notes", new List<AccessibilityNode> { leaf }, []);
        var (snapshot, action, _) = Slice(tree);
        var captured = snapshot.Run("Notes", "session-a", TreeOptions.Default(), 100);
        var snapshotId = SnapshotIdOf(captured);

        var response = action.Run("click", "@" + snapshotId + ":e1", new Action { Kind = ActionKind.Click }, "session-b");
        var json = response.ToJsonString();

        Assert.Contains("\"code\":\"SNAPSHOT_NOT_FOUND\"", json);
    }

    [Fact]
    public void RunningOutOfBudgetReturnsAPartialTreeNotAFailure()
    {
        var first = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var second = Node("button", "Stop", new List<AccessibilityNode>(), ["Press"]);
        var tree = Node("group", "Notes", new List<AccessibilityNode> { first, second }, []);
        var (snapshot, _, _) = Slice(tree);

        var json = snapshot.Run("Notes", null, TreeOptions.Default(), 1).ToJsonString();

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"complete\":false", json);
        Assert.Contains("\"truncated\":true", json);
        Assert.Contains("\"subtree_truncated\":true", json);
    }

    [Fact]
    public void NativeAutomationIdSerializesAsTaggedObjectAndDescriptorsGuardCompactCollapse()
    {
        var leaf = new AccessibilityNode
        {
            Role = "button",
            Identity = new NodeIdentity
            {
                Name = "Send",
                NativeId = new ElementIdentifier(IdentifierKind.AutomationId, "send-btn"),
            },
            Presentation = new NodePresentation
            {
                AvailableActions = ["Press"],
                Bounds = new Rect(0, 0, 10, 10),
            },
        };
        var wrapperWithSubrole = new AccessibilityNode
        {
            Role = "group",
            Presentation = new NodePresentation
            {
                Descriptors = new NodeDescriptor { Subrole = "navigation" },
                States = ["enabled"],
            },
            Children = [leaf],
        };
        var tree = Node("window", "Notes", [wrapperWithSubrole], []);
        var (snapshot, _, _) = Slice(tree);

        var options = TreeOptions.Default() with { Compact = true };
        var json = snapshot.Run("Notes", null, options, 100).ToJsonString();

        Assert.Contains("\"native_id\":{\"kind\":\"automation_id\",\"value\":\"send-btn\"}", json);
        Assert.Contains("\"subrole\":\"navigation\"", json);
    }
}