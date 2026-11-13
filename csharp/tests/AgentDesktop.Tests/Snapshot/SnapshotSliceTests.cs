using System.IO;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
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

    public Func<ActionRequest, ActionResult>? OnAction { get; set; }

    public List<AppInfo> ListApps(Deadline deadline) => new() { App };

    public List<WindowInfo> ListWindows(WindowFilter filter, Deadline deadline) => new() { Window };

    public ObservedTree ObserveTree(ObservationRoot root, ObservationRequest request) =>
        new(Tree, Complete, false, 0);

    public bool Complete { get; set; } = true;

    public NativeHandle ResolveElementStrict(RefEntry entry, Deadline deadline)
    {
        if (!entry.Capabilities.UniqueAtCapture || !entry.Identity.IsCaptureUnique)
        {
            var reason = entry.Identity.UniquenessReason ?? entry.UniquenessReason ?? "ref was not unique at capture";
            throw new AdapterError(ErrorCode.AmbiguousTarget, $"Ref cannot be resolved strictly: {reason}")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        ResolveCalls++;
        return NativeHandle.New(entry.Identity.Role);
    }

    public ActionResult ExecuteAction(NativeHandle handle, ActionRequest request, InteractionLease lease)
    {
        LastRequest = request;
        return OnAction is not null
            ? OnAction(request)
            : ActionResult.DeliveredUnverified(request.Action.Name());
    }
}

public class SnapshotSliceTests
{
    private static (SnapshotCommand, RefActionCommand, StateRoot, FakeAdapter) Slice(AccessibilityNode tree, bool complete = true)
    {
        var app = new AppInfo { Name = "Notes", Pid = ProcessId.From(42) };
        var window = new WindowInfo { Id = "w1", Title = "Notes", App = "Notes", Pid = app.Pid };
        var adapter = new FakeAdapter(app, window, tree);
        adapter.Complete = complete;
        var root = new StateRoot(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ad-slice-" + Guid.NewGuid().ToString("N")));
        var sessions = new SessionStore(root);
        var snapshots = new SnapshotStore(root);
        return (new SnapshotCommand(adapter, snapshots, sessions), new RefActionCommand(adapter, snapshots, sessions), root, adapter);
    }

    private static string SnapshotIdOf(EnvelopeResponse response) =>
        JsonNode.Parse(response.ToJsonString())!["data"]!["snapshot_id"]!.GetValue<string>();

    private static AccessibilityNode Node(string role, string name, List<AccessibilityNode> children, List<string> actions) =>
        new()
        {
            Role = role,
            Identity = new NodeIdentity { Name = AgentDesktop.Core.Locator.LocatorField<string>.Known(name) },
            Presentation = new NodePresentation { AvailableActions = actions, Bounds = new Rect(0, 0, 10, 10) },
            Children = children,
        };

    /// <summary>A node whose walk recorded a boundary child count, which is the frame shape the
    /// capture plans a frame for. A leaf the walk finished is not a frame, so its own count is
    /// not recorded.</summary>
    private static AccessibilityNode Frame(string role, string name, List<AccessibilityNode> children, List<string> actions) =>
        Node(role, name, children, actions) with { ChildrenCount = (uint)children.Count };

    [Fact]
    public void SnapshotAllocatesRefsDepthFirstAndPersistsTheRefMap()
    {
        var first = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var second = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Frame("group", "Notes", new List<AccessibilityNode> { first, second }, []);
        var (snapshot, _, root, _) = Slice(tree);

        var response = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var json = response.ToJsonString();
        var persisted = SnapshotIdOf(response);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"ref_count\":2", json);
        Assert.Contains("\"complete\":true", json);
        Assert.Contains("\"ref_id\":\"@" + persisted + ":e1\"", json);
        Assert.Contains("\"ref_id\":\"@" + persisted + ":e2\"", json);
        Assert.True(File.Exists(System.IO.Path.Combine(root.Snapshots, "default", persisted, "refmap.json")));
    }

    /// <summary>A ref-able leaf is given a ref, and the capture proof records that the window
    /// proved it unique. The census does not suppress the ref: a ref action is what refuses a
    /// proof that is not unique, so the entry carries the proof, not the tree.</summary>
    [Fact]
    public void AProvenUniqueLeafIsGivenARefAndCarriesItsProof()
    {
        var leaf = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Frame("group", "Notes", new List<AccessibilityNode> { leaf }, []);
        var (snapshot, _, root, _) = Slice(tree);

        var response = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var json = response.ToJsonString();
        var persisted = SnapshotIdOf(response);
        var refmap = File.ReadAllText(System.IO.Path.Combine(root.Snapshots, "default", persisted, "refmap.json"));

        Assert.Contains("\"ref_count\":1", json);
        Assert.Contains("\"ref_id\":\"@" + persisted + ":e1\"", json);
        Assert.Contains("\"UniqueAtCapture\":true", refmap);
        Assert.Contains("\"UniquenessReason\":null", refmap);
    }

    /// <summary>A frame the capture proved unique is given a ref, because it is the boundary
    /// resolution reaches its subtree through.</summary>
    [Fact]
    public void AProvenUniqueFrameIsGivenARef()
    {
        var child = Node("listitem", "Result", new List<AccessibilityNode>(), []);
        var box = Frame("combobox", "Query", new List<AccessibilityNode> { child }, ["Expand"]);
        var tree = Frame("group", "Notes", new List<AccessibilityNode> { box }, []);
        var (snapshot, _, _, _) = Slice(tree);

        var response = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var persisted = SnapshotIdOf(response);

        Assert.Contains("\"ref_count\":1", response.ToJsonString());
        Assert.Contains("\"ref_id\":\"@" + persisted + ":e1\"", response.ToJsonString());
    }

    [Fact]
    public void ARefActionResolvesFromThePersistedCaptureProofAndReturnsTheReport()
    {
        var child = Node("listitem", "Result", new List<AccessibilityNode>(), []);
        var box = Frame("combobox", "Query", new List<AccessibilityNode> { child }, ["Expand"]);
        var tree = Frame("group", "Notes", new List<AccessibilityNode> { box }, []);
        var (snapshot, action, _, _) = Slice(tree);

        var captured = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var snapshotId = SnapshotIdOf(captured);

        var response = action.Run("click", "@" + snapshotId + ":e1", new Action { Kind = ActionKind.Click }, null);
        var json = response.ToJsonString();

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"action\":\"click\"", json);
        Assert.Contains("\"disposition\":{\"delivery\":\"delivered_unverified\",\"retry\":\"unsafe\"}", json);
    }

    [Fact]
    public void ValueWriteActionSerializesVerifiedStepsAndElementValueCommitDetails()
    {
        var field = Frame("textfield", "Query", new List<AccessibilityNode> { Node("text", "", new List<AccessibilityNode>(), []) }, ["SetValue"]);
        var tree = Frame("group", "Notes", new List<AccessibilityNode> { field }, []);
        var (snapshot, action, _, adapter) = Slice(tree);
        adapter.OnAction = request => ActionResult.FromExecution(
            request.Action,
            new List<ActionStep> { new("ValuePattern.SetValue", "succeeded", "semantic_api", true) });

        var captured = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var snapshotId = SnapshotIdOf(captured);

        var response = action.Run(
            "set-value",
            "@" + snapshotId + ":e1",
            new Action { Kind = ActionKind.SetValue, Text = "hello" },
            null,
            headed: true);
        var json = response.ToJsonString();

        Assert.True(adapter.LastRequest?.Headed);
        Assert.Contains("\"delivery\":\"delivered_verified\"", json);
        Assert.Contains("\"verified\":true", json);
        Assert.Contains("\"verification_scope\":\"element_value\"", json);
        Assert.Contains("\"application_commit\":\"not_verified\"", json);
    }

    [Fact]
    public void DuplicateCapturedIdentitiesMarkBothRefsNonUniqueAndFailStrictResolution()
    {
        var first = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var second = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Node("group", "Notes", new List<AccessibilityNode> { first, second }, []);
        var (snapshot, action, _, _) = Slice(tree);

        var captured = snapshot.Run("Notes", null, TreeOptions.Default(), 100);
        var snapshotId = SnapshotIdOf(captured);

        var response = action.Run("click", "@" + snapshotId + ":e1", new Action { Kind = ActionKind.Click }, null);
        var json = response.ToJsonString();

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("\"code\":\"AMBIGUOUS_TARGET\"", json);
        Assert.Contains("was not unique in the observation it was captured from", json);
    }

    [Fact]
    public void ABareRefIsRejectedAsInvalidArgsBeforeTheApplicationIsRead()
    {
        var leaf = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Node("group", "Notes", new List<AccessibilityNode> { leaf }, []);
        var (snapshot, action, _, _) = Slice(tree);
        var captured = snapshot.Run("Notes", null, TreeOptions.Default(), 100);

        var response = action.Run("click", "e1", new Action { Kind = ActionKind.Click }, null);
        var json = response.ToJsonString();

        Assert.Contains("\"ok\":false", json);
        Assert.Contains("\"code\":\"INVALID_ARGS\"", json);
    }

    [Fact]
    public void ARefFromAnotherSessionsNamespaceIsNotFound()
    {
        var leaf = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var twin = Node("button", "Run", new List<AccessibilityNode>(), ["Press"]);
        var tree = Frame("group", "Notes", new List<AccessibilityNode> { leaf, twin }, []);
        var (snapshot, action, _, _) = Slice(tree);
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
        var tree = Frame("group", "Notes", new List<AccessibilityNode> { first, second }, []);
        var (snapshot, action, _, _) = Slice(tree, complete: false);

        var captured = snapshot.Run("Notes", null, TreeOptions.Default(), 1);
        var json = captured.ToJsonString();
        var snapshotId = SnapshotIdOf(captured);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"complete\":false", json);
        Assert.Contains("\"truncated\":true", json);
        Assert.Contains("\"subtree_truncated\":true", json);

        var resolved = action.Run("click", "@" + snapshotId + ":e1", new Action { Kind = ActionKind.Click }, null);
        Assert.Contains("\"code\":\"AMBIGUOUS_TARGET\"", resolved.ToJsonString());
    }

    /// <summary>A ref records the child indexes that lead from the window root to the element it
    /// names, so a locator anchor can descend the stored path. A refmap whose entries carry no path
    /// makes every anchor land on the window root, so the ref resolves to the wrong element.</summary>
    [Fact]
    public void ARefRecordsTheChildIndexesFromTheWindowRoot()
    {
        var deep = Node("button", "Send", new List<AccessibilityNode>(), ["Press"]);
        var row = Frame("group", "Row", new List<AccessibilityNode> {
            Node("button", "One", new List<AccessibilityNode>(), ["Press"]),
            Node("button", "Two", new List<AccessibilityNode>(), ["Press"]),
            deep }, []);
        var toolbar = Frame("group", "Toolbar", new List<AccessibilityNode> {
            Frame("group", "Leading", new List<AccessibilityNode>(), []),
            row }, []);
        var tree = Frame("window", "Notes", new List<AccessibilityNode> {
            Frame("group", "Side", new List<AccessibilityNode>(), []),
            toolbar }, []);
        var (snapshot, _, root, _) = Slice(tree);

        var persisted = SnapshotIdOf(snapshot.Run("Notes", null, TreeOptions.Default(), 100));
        var refmap = File.ReadAllText(System.IO.Path.Combine(root.Snapshots, "default", persisted, "refmap.json"));

        Assert.Contains("\"Path\":[1,1,2]", refmap);
    }

    [Fact]
    public void NativeAutomationIdSerializesAsTaggedObjectAndDescriptorsGuardCompactCollapse()
    {
        var leaf = new AccessibilityNode
        {
            Role = "button",
            Identity = new NodeIdentity
            {
                Name = AgentDesktop.Core.Locator.LocatorField<string>.Known("Send"),
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
        var (snapshot, _, _, _) = Slice(tree);

        var options = TreeOptions.Default() with { Compact = true };
        var json = snapshot.Run("Notes", null, options, 100).ToJsonString();

        Assert.Contains("\"native_id\":{\"kind\":\"automation_id\",\"value\":\"send-btn\"}", json);
        Assert.Contains("\"subrole\":\"navigation\"", json);
    }

    /// <summary>The capture record is what a state property answers from when the live read finds
    /// nothing, so the states the observation saw have to reach the persisted refmap.</summary>
    [Fact]
    public void TheRefMapRecordsTheStatesTheCaptureObserved()
    {
        var node = Node("checkbox", "Bold", [], ["Toggle"]) with
        {
            Presentation = new NodePresentation
            {
                AvailableActions = ["Toggle"],
                Bounds = new Rect(0, 0, 10, 10),
                States = ["checked"],
            },
        };
        var (snapshot, _, root, _) = Slice(node);

        var persisted = SnapshotIdOf(snapshot.Run("Notes", null, TreeOptions.Default(), 100));
        var map = new SnapshotStore(root).Load(persisted, new SessionStore(root).Namespace(null));

        Assert.Equal(["checked"], map!.Entries[0].Capabilities.States);
    }
}


