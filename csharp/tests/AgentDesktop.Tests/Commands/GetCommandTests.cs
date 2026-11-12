using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Commands;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using Xunit;

namespace AgentDesktop.Tests.Commands;

/// <summary>A stub adapter with a live read the test controls: the value, state and bounds a
/// get resolves, and whether the reads are refused.</summary>
public sealed class LiveReadStub : PlatformAdapter
{
    public string? LiveValue { get; set; } = "live value";

    public ElementState? LiveState { get; set; } = new() { Role = "button", States = new List<string> { "focused" } };

    public Rect? LiveBounds { get; set; } = new Rect(10, 20, 30, 40);

    public bool RefuseLiveReads { get; set; }

    public int LiveReadCalls { get; private set; }

    public NativeHandle ResolveElementStrict(RefEntry entry, Deadline deadline)
    {
        if (!entry.Capabilities.UniqueAtCapture || !entry.Identity.IsCaptureUnique)
        {
            throw new AdapterError(ErrorCode.AmbiguousTarget, "ref was not unique at capture")
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        return NativeHandle.New(entry.Identity.Role);
    }

    public string? GetLiveValue(NativeHandle handle, Deadline deadline)
    {
        LiveReadCalls++;
        if (RefuseLiveReads)
        {
            throw AdapterError.NotSupported("get_live_value");
        }

        return LiveValue;
    }

    public ElementState? GetLiveState(NativeHandle handle, Deadline deadline)
    {
        LiveReadCalls++;
        if (RefuseLiveReads)
        {
            throw AdapterError.NotSupported("get_live_state");
        }

        return LiveState;
    }

    public Rect? GetElementBounds(NativeHandle handle, Deadline deadline)
    {
        LiveReadCalls++;
        if (RefuseLiveReads)
        {
            throw AdapterError.NotSupported("get_element_bounds");
        }

        return LiveBounds;
    }
}

public class GetCommandTests
{
    private const string SnapshotId = "s1";

    private static (GetCommand, LiveReadStub, StateRoot) Slice(RefEntry entry)
    {
        var root = new StateRoot(Path.Combine(Path.GetTempPath(), "ad-get-" + Guid.NewGuid().ToString("N")));
        var sessions = new SessionStore(root);
        var snapshots = new SnapshotStore(root);
        var map = new RefMap(SnapshotId);
        map.Add(entry);
        snapshots.Save(map, sessions.Namespace(null));

        var stub = new LiveReadStub();
        return (new GetCommand(stub, snapshots, sessions), stub, root);
    }

    private static RefEntry Entry(string role, string? name, Rect? recordedBounds) => new()
    {
        Identity = new RefEntryIdentity
        {
            Role = role,
            StableName = name is null ? LocatorField<string>.Absent() : LocatorField<string>.Known(name),
            WindowKey = "w1",
            UniquenessReason = null,
        },
        Geometry = new RefGeometry { Bounds = recordedBounds },
        Capabilities = new RefCapabilities { UniqueAtCapture = true },
    };

    /// <summary>Runs one get and writes the envelope. A resolution refusal reaches the caller as a
    /// thrown AdapterError, which is what the CLI catches, so the helper settles it into the
    /// envelope the command prints.</summary>
    private static JsonNode Run(GetCommand command, string property)
    {
        try
        {
            return JsonNode.Parse(Envelope.Write(command.Run("get", $"@{SnapshotId}:e1", property, null)))!;
        }
        catch (AdapterError error)
        {
            return JsonNode.Parse(Envelope.Write(Envelope.Err("get", error)))!;
        }
    }

    private static string CodeOf(JsonNode response) => response["error"]!["code"]!.GetValue<string>();

    private static JsonObject DataOf(JsonNode response) => response["data"]!.AsObject();

    private static string TextOf(JsonNode response) => DataOf(response)["value"]!.GetValue<string>();

    private static bool LiveFlagOf(JsonNode response) => DataOf(response)["live"]!.GetValue<bool>();

    [Fact]
    public void RoleAndTitleComeFromTheCaptureRecordAndReadNothingLive()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));

        var role = Run(command, "role");
        var title = Run(command, "title");

        Assert.Equal("button", TextOf(role));
        Assert.Equal("Run", TextOf(title));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void TextPrefersTheRecordedNameForANonValueRole()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));

        var response = Run(command, "text");

        Assert.Equal("Run", TextOf(response));
        Assert.False(DataOf(response).ContainsKey("live"));
    }

    [Fact]
    public void TextPrefersTheLiveValueForAnEditableTextRole()
    {
        var (command, stub, _) = Slice(Entry("textfield", "Search box", null));
        stub.LiveValue = "typed query";

        var response = Run(command, "text");

        Assert.Equal("typed query", TextOf(response));
    }

    [Fact]
    public void TextFallsBackToTheLiveValueWhenTheCaptureRecordedNoName()
    {
        var (command, _, _) = Slice(Entry("button", null, null));

        var response = Run(command, "text");

        Assert.Equal("live value", TextOf(response));
    }

    [Fact]
    public void BoundsSayWhetherTheAnswerCameFromTheLiveReadOrTheCapture()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", new Rect(1, 2, 3, 4)));

        var live = Run(command, "bounds");
        stub.RefuseLiveReads = true;
        var recorded = Run(command, "bounds");

        Assert.True(LiveFlagOf(live));
        Assert.Equal(10, DataOf(live)["value"]!["x"]!.GetValue<double>());
        Assert.False(LiveFlagOf(recorded));
        Assert.Equal(1, DataOf(recorded)["value"]!["x"]!.GetValue<double>());
    }

    [Fact]
    public void ValueReportsAnEmptyLiveValueAsTheAnswer()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));
        stub.LiveValue = "";

        var response = Run(command, "value");

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Equal("", TextOf(response));
        Assert.True(LiveFlagOf(response));
    }

    [Fact]
    public void StatesReportsAnEmptyLiveStateListAsTheAnswer()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));
        stub.LiveState = new ElementState { Role = "button", States = new List<string>() };

        var response = Run(command, "states");

        Assert.True(response["ok"]!.GetValue<bool>());
        Assert.Empty(DataOf(response)["value"]!.AsArray());
    }

    [Fact]
    public void APropertyTheCaptureNeverRecordedReportsThePlatformsRefusal()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));
        stub.RefuseLiveReads = true;

        var response = Run(command, "value");

        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Equal("PLATFORM_NOT_SUPPORTED", CodeOf(response));
    }

    [Fact]
    public void TextStillAnswersWhenTheLiveReadIsRefused()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));
        stub.RefuseLiveReads = true;

        var response = Run(command, "text");

        Assert.Equal("Run", TextOf(response));
    }

    [Fact]
    public void ARefTheCaptureDidNotProveUniqueIsRefusedBeforeTheApplicationIsRead()
    {
        var entry = Entry("button", "Run", null) with
        {
            Identity = new RefEntryIdentity
            {
                Role = "button",
                StableName = LocatorField<string>.Known("Run"),
                WindowKey = "w1",
                UniquenessReason = "two elements share these fields",
            },
        };
        var (command, stub, _) = Slice(entry);

        var response = Run(command, "text");

        Assert.Equal("AMBIGUOUS_TARGET", CodeOf(response));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void ABareRefIsRejectedBeforeTheStoreIsRead()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));

        var response = JsonNode.Parse(Envelope.Write(command.Run("get", "e1", "text", null)))!;

        Assert.Equal("INVALID_ARGS", CodeOf(response));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void AnUnknownPropertyIsRejectedBeforeTheApplicationIsRead()
    {
        var (command, stub, _) = Slice(Entry("button", "Run", null));

        var response = Run(command, "color");

        Assert.Equal("INVALID_ARGS", CodeOf(response));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void ASnapshotOutsideTheSessionNamespaceIsReportedAsNotFound()
    {
        var (command, _, _) = Slice(Entry("button", "Run", null));

        var response = JsonNode.Parse(Envelope.Write(command.Run("get", "@missing:e1", "text", null)))!;

        Assert.Equal("SNAPSHOT_NOT_FOUND", CodeOf(response));
    }
}
