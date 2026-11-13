using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Commands;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using Xunit;

namespace AgentDesktop.Tests.Commands;

/// <summary>A stub adapter whose live state, bounds, and refusal the test controls. A refused
/// read is the platform saying it cannot do the read; a null answer is the platform reading the
/// element and finding nothing.</summary>
public sealed class IsStub : PlatformAdapter
{
    public ElementState? LiveState { get; set; } = new() { Role = "checkbox", States = new List<string>() };

    public Rect? LiveBounds { get; set; } = new Rect(0, 0, 10, 10);

    public bool RefuseState { get; set; }

    public bool RefuseBounds { get; set; }

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

    public ElementState? GetLiveState(NativeHandle handle, Deadline deadline)
    {
        if (RefuseState)
        {
            throw AdapterError.NotSupported("get_live_state");
        }

        LiveReadCalls++;
        return LiveState;
    }

    public Rect? GetElementBounds(NativeHandle handle, Deadline deadline)
    {
        if (RefuseBounds)
        {
            throw AdapterError.NotSupported("get_element_bounds");
        }

        LiveReadCalls++;
        return LiveBounds;
    }
}

public class IsCommandTests
{
    private const string SnapshotId = "s1";

    private static (IsCommand, IsStub) Slice(RefEntry entry)
    {
        var root = new StateRoot(Path.Combine(Path.GetTempPath(), "ad-is-" + Guid.NewGuid().ToString("N")));
        var sessions = new SessionStore(root);
        var snapshots = new SnapshotStore(root);
        var map = new RefMap(SnapshotId);
        map.Add(entry);
        snapshots.Save(map, sessions.Namespace(null));

        var stub = new IsStub();
        return (new IsCommand(stub, snapshots, sessions), stub);
    }

    private static RefEntry Entry(string role, List<string> actions, List<string>? recorded = null) => new()
    {
        Identity = new RefEntryIdentity
        {
            Role = role,
            StableName = LocatorField<string>.Known("Target"),
            WindowKey = "w1",
            UniquenessReason = null,
        },
        Geometry = new RefGeometry(),
        Capabilities = new RefCapabilities
        {
            States = recorded ?? new List<string>(),
            UniqueAtCapture = true,
            AvailableActions = actions,
        },
    };

    /// <summary>Runs one is and writes the envelope. A resolution refusal reaches the caller as a
    /// thrown AdapterError, which is what the CLI catches, so the helper settles it into the
    /// envelope the command prints.</summary>
    private static JsonNode Run(IsCommand command, string property, string? qualified = null)
    {
        var target = qualified ?? $"@{SnapshotId}:e1";
        try
        {
            return JsonNode.Parse(Envelope.Write(command.Run("is", target, property, null)))!;
        }
        catch (AdapterError error)
        {
            return JsonNode.Parse(Envelope.Write(Envelope.Err("is", error)))!;
        }
    }

    private static string CodeOf(JsonNode response) => response["error"]!["code"]!.GetValue<string>();

    private static JsonObject DataOf(JsonNode response) => response["data"]!.AsObject();

    private static (bool Result, bool Applicable) Answer(JsonNode response, string property)
    {
        var data = DataOf(response);
        Assert.Equal(property, data["property"]!.GetValue<string>());
        return (data["result"]!.GetValue<bool>(), data["applicable"]!.GetValue<bool>());
    }

    private static IsCommand CommandFor(string role, List<string> actions, ElementState? state = null,
        Rect? bounds = null, bool refuseState = false, bool refuseBounds = false,
        List<string>? recorded = null)
    {
        var (command, stub) = Slice(Entry(role, actions, recorded));
        stub.LiveState = state;
        stub.LiveBounds = bounds;
        stub.RefuseState = refuseState;
        stub.RefuseBounds = refuseBounds;
        return command;
    }

    private static Rect OnScreen => new Rect(0, 0, 10, 10);

    [Fact]
    public void AHiddenElementReportsNotVisible()
    {
        var command = CommandFor("checkbox", new List<string>(),
            new ElementState { Role = "checkbox", States = new List<string> { "hidden" } }, OnScreen);

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.False(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void ZeroSizedBoundsReportNotVisible()
    {
        var command = CommandFor("checkbox", new List<string>(),
            new ElementState { Role = "checkbox", States = new List<string>() },
            new Rect(0, 0, 0, 10));

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.False(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void AnOffscreenElementReportsNotVisible()
    {
        var command = CommandFor("checkbox", new List<string>(),
            new ElementState { Role = "checkbox", States = new List<string> { "offscreen" } }, OnScreen);

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.False(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void AVisibleElementWithLiveEvidenceReportsTrue()
    {
        var command = CommandFor("checkbox", new List<string>(),
            new ElementState { Role = "checkbox", States = new List<string>() }, OnScreen);

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.True(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void VisibleDegradesApplicabilityWhenTheLiveReadsAreRefused()
    {
        var command = CommandFor("checkbox", new List<string>(), refuseState: true, refuseBounds: true);

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.False(answer.Result);
        Assert.False(answer.Applicable);
    }

    [Fact]
    public void VisibleAgreesWithTheActionabilityGateWhenBoundsCannotBeReadAndOffscreenIsKnownFalse()
    {
        var command = CommandFor("checkbox", new List<string>(),
            new ElementState { Role = "checkbox", States = new List<string>(), Offscreen = false }, null);

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.True(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void VisibleStaysInapplicableWhenBoundsCannotBeReadAndOffscreenIsUnknown()
    {
        var command = CommandFor("checkbox", new List<string>(),
            new ElementState { Role = "checkbox", States = new List<string>() }, null);

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.False(answer.Result);
        Assert.False(answer.Applicable);
    }

    [Fact]
    public void CheckedUsesTheLiveState()
    {
        var command = CommandFor("checkbox", new List<string> { "Toggle" },
            new ElementState { Role = "checkbox", States = new List<string> { "checked" }, Enabled = true });

        var answer = Answer(Run(command, "checked"), "checked");

        Assert.True(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void CheckedDoesNotInferAStateTheLiveReadDidNotReport()
    {
        var command = CommandFor("checkbox", new List<string> { "Toggle" }, null);

        var answer = Answer(Run(command, "checked"), "checked");

        Assert.False(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void CheckedIsApplicableForAToggleableRoleEvenWhenTheLiveReadIsRefused()
    {
        var command = CommandFor("switch", new List<string> { "Toggle" }, refuseState: true);

        var answer = Answer(Run(command, "checked"), "checked");

        Assert.False(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void CheckedFallsBackToTheStateTheCaptureRecordedWhenTheLiveReadFindsNothing()
    {
        var command = CommandFor("checkbox", new List<string> { "Toggle" }, null,
            recorded: new List<string> { "checked" });

        var answer = Answer(Run(command, "checked"), "checked");

        Assert.True(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void FocusedFallsBackToTheStateTheCaptureRecordedWhenTheLiveReadIsRefused()
    {
        var command = CommandFor("button", new List<string> { "Click" }, refuseState: true,
            recorded: new List<string> { "focused" });

        var answer = Answer(Run(command, "focused"), "focused");

        Assert.True(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void RecordedStatesNeverMakeVisibleApplicableWithoutLiveEvidence()
    {
        var command = CommandFor("checkbox", new List<string>(), null, null,
            recorded: new List<string> { "checked" });

        var answer = Answer(Run(command, "visible"), "visible");

        Assert.False(answer.Result);
        Assert.False(answer.Applicable);
    }

    [Fact]
    public void CheckedIsInapplicableForARoleThatCarriesNoToggle()
    {
        var command = CommandFor("button", new List<string> { "Click" },
            new ElementState { Role = "button", States = new List<string>() });

        var answer = Answer(Run(command, "checked"), "checked");

        Assert.False(answer.Result);
        Assert.False(answer.Applicable);
    }

    [Fact]
    public void ExpandedUsesTheLiveState()
    {
        var command = CommandFor("combobox", new List<string> { "Expand", "Collapse" },
            new ElementState { Role = "combobox", States = new List<string> { "expanded" } });

        var answer = Answer(Run(command, "expanded"), "expanded");

        Assert.True(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void ExpandedIsApplicableForAnExpandableRoleAndReportsTheLiveAnswer()
    {
        var command = CommandFor("treeitem", new List<string> { "Expand", "Collapse" },
            new ElementState { Role = "treeitem", States = new List<string>() });

        var answer = Answer(Run(command, "expanded"), "expanded");

        Assert.False(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void ExpandedIsInapplicableWhenNeitherTheRoleNorTheAdvertisedActionsCarryIt()
    {
        var command = CommandFor("button", new List<string> { "Click" },
            new ElementState { Role = "button", States = new List<string>() });

        var answer = Answer(Run(command, "expanded"), "expanded");

        Assert.False(answer.Result);
        Assert.False(answer.Applicable);
    }

    [Fact]
    public void EnabledReportsFalseWhenTheLiveReadAnsweredNothing()
    {
        var command = CommandFor("button", new List<string> { "Click" }, null);

        var answer = Answer(Run(command, "enabled"), "enabled");

        Assert.False(answer.Result);
        Assert.False(answer.Applicable);
    }

    [Fact]
    public void EnabledIsTrueOnlyWhenTheLiveReadReportedEnabled()
    {
        var enabled = CommandFor("button", new List<string> { "Click" },
            new ElementState { Role = "button", States = new List<string>(), Enabled = true });
        var disabled = CommandFor("button", new List<string> { "Click" },
            new ElementState { Role = "button", States = new List<string>(), Enabled = false });

        Assert.Equal((true, true), Answer(Run(enabled, "enabled"), "enabled"));
        Assert.Equal((false, true), Answer(Run(disabled, "enabled"), "enabled"));
    }

    [Fact]
    public void TheStatePredicatesReadTheLiveStateList()
    {
        var command = CommandFor("button", new List<string> { "Click" },
            new ElementState
            {
                Role = "button",
                States = new List<string> { "focused", "expanded", "selected" },
                Enabled = true,
            });

        Assert.Equal((true, true), Answer(Run(command, "focused"), "focused"));
        Assert.Equal((true, true), Answer(Run(command, "selected"), "selected"));
    }

    [Fact]
    public void FocusedIsAlwaysApplicableAndReportsTheLiveState()
    {
        var command = CommandFor("button", new List<string> { "Click" },
            new ElementState { Role = "button", States = new List<string>() });

        var answer = Answer(Run(command, "focused"), "focused");

        Assert.False(answer.Result);
        Assert.True(answer.Applicable);
    }

    [Fact]
    public void ARefTheCaptureDidNotProveUniqueIsRefusedBeforeTheApplicationIsRead()
    {
        var entry = Entry("checkbox", new List<string> { "Toggle" }) with
        {
            Identity = new RefEntryIdentity
            {
                Role = "checkbox",
                StableName = LocatorField<string>.Known("Target"),
                WindowKey = "w1",
                UniquenessReason = "two elements share these fields",
            },
        };
        var (command, stub) = Slice(entry);

        var response = Run(command, "visible");

        Assert.Equal("AMBIGUOUS_TARGET", CodeOf(response));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void ABareRefIsRejectedBeforeTheStoreIsRead()
    {
        var (command, stub) = Slice(Entry("checkbox", new List<string>()));

        var response = Run(command, "visible", "e1");

        Assert.Equal("INVALID_ARGS", CodeOf(response));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void AnUnknownPropertyIsRejectedAsInvalidArgs()
    {
        var (command, stub) = Slice(Entry("checkbox", new List<string>()));

        var response = Run(command, "color");

        Assert.Equal("INVALID_ARGS", CodeOf(response));
        Assert.Equal(2, stub.LiveReadCalls);
    }

    [Fact]
    public void AnUnknownSnapshotIsReportedAsNotFoundBeforeTheApplicationIsRead()
    {
        var (command, stub) = Slice(Entry("checkbox", new List<string>()));

        var response = Run(command, "visible", "@missing:e1");

        Assert.Equal("SNAPSHOT_NOT_FOUND", CodeOf(response));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void ARefOutsideTheSnapshotIsReportedStaleBeforeTheApplicationIsRead()
    {
        var (command, stub) = Slice(Entry("checkbox", new List<string>()));

        var response = Run(command, "visible", $"@{SnapshotId}:e9");

        Assert.Equal("STALE_REF", CodeOf(response));
        Assert.Equal(0, stub.LiveReadCalls);
    }

    [Fact]
    public void EveryAnswerNamesThePropertyAndTheRefItWasAskedAbout()
    {
        var command = CommandFor("checkbox", new List<string> { "Toggle" },
            new ElementState { Role = "checkbox", States = new List<string> { "checked" }, Enabled = true });

        var data = DataOf(Run(command, "checked"));

        Assert.Equal("checked", data["property"]!.GetValue<string>());
        Assert.Equal($"@{SnapshotId}:e1", data["ref"]!.GetValue<string>());
    }

    /// <summary>A ref whose snapshot is missing must say nothing was delivered and point at the
    /// session scope, matching the Rust contract. Without a safe-retry disposition the envelope
    /// writer suppresses recovery, so a read-only failure cannot tell the caller how to recover.</summary>
    [Fact]
    public void IsCommandReportsSnapshotNotFoundWithNotDeliveredDispositionAndRecovery()
    {
        var (command, _) = Slice(Entry("checkbox", new List<string> { "Click" }));
        var envelope = Run(command, "checked", "@s9:e1");
        var error = envelope["error"]!.AsObject();

        Assert.Equal("SNAPSHOT_NOT_FOUND", error["code"]!.GetValue<string>());
        Assert.Equal("not_delivered", error["disposition"]!["delivery"]!.GetValue<string>());
        Assert.Equal("safe", error["disposition"]!["retry"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(error["suggestion"]!.GetValue<string>()));
        Assert.Equal("refresh_snapshot_then_retry_original", error["recovery"]!["strategy"]!.GetValue<string>());
        Assert.True(error["recovery"]!["retryable"]!.GetValue<bool>());
        Assert.True(error["recovery"]!["requires_fresh_snapshot"]!.GetValue<bool>());
    }

    /// <summary>A ref that is in the snapshot store but not in the map is stale, and a stale read
    /// must carry the same delivery disposition as the Rust side.</summary>
    [Fact]
    public void IsCommandReportsStaleRefWithNotDeliveredDispositionAndRecovery()
    {
        var (command, _) = Slice(Entry("checkbox", new List<string> { "Click" }));
        var envelope = Run(command, "checked", $"@{SnapshotId}:e99");
        var error = envelope["error"]!.AsObject();

        Assert.Equal("STALE_REF", error["code"]!.GetValue<string>());
        Assert.Equal("not_delivered", error["disposition"]!["delivery"]!.GetValue<string>());
        Assert.Equal("safe", error["disposition"]!["retry"]!.GetValue<string>());
        Assert.Equal("refresh_snapshot_then_retry_original", error["recovery"]!["strategy"]!.GetValue<string>());
    }
}
