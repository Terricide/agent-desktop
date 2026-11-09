using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Actions;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.Uia;
using Xunit;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Windows.Tests.Actions;

/// <summary>Verifies the null-handle key path: a global chord is accepted and emitted, a
/// standalone key hold is refused with the key left unheld, and no other action reaches past
/// the missing element. The `SendInput` seam is replaced with a recorder so no real key event
/// reaches the desktop.</summary>
[Collection(AgentDesktop.Windows.Tests.KeyboardSendSinkCollection.Name)]
public sealed class GlobalKeyPathTests : IDisposable
{
    private readonly List<KeyboardInputEvent> _posted = [];
    private readonly Action<IReadOnlyList<KeyboardInputEvent>> _originalSink;

    public GlobalKeyPathTests()
    {
        _originalSink = KeyboardSend.PostSink;
        KeyboardSend.PostSink = events => _posted.AddRange(events);
    }

    public void Dispose() => KeyboardSend.PostSink = _originalSink;

    private static InteractionLease Lease() =>
        new(Deadline.After(5000), InteractionPolicy.Physical);

    private static ActionResult Run(ActionKind kind, KeyCombo combo) =>
        new ActionsAdapter().ExecuteAction(
            NativeHandle.Null(),
            new ActionRequest(new Action { Kind = kind, Key = combo }, true),
            Lease());

    [Fact]
    public void GlobalPressPressesTheModifiersThenTheKeyAndReleasesInReverseOrder()
    {
        var result = Run(ActionKind.PressKey, Combo.Parse("ctrl+a"));

        Assert.Equal([("PressKey", "succeeded", "physical_synthetic")],
            result.Steps.Select(s => (s.Label, s.Outcome, s.Mechanism)));
        Assert.Equal(Delivery.DeliveredUnverified, result.Disposition.Delivery);
        Assert.Equal(
            [(0x11, true), (0x41, true), (0x41, false), (0x11, false)],
            _posted.Select(e => (e.Vk, (e.Flags & KeyboardIds.KeyEventKeyUp) == 0)));
    }

    [Fact]
    public void StandaloneKeyDownIsRefusedWithTheKeyLeftUnheld()
    {
        var error = Assert.Throws<AdapterError>(() => Run(ActionKind.KeyDown, Combo.Parse("ctrl+a")));

        Assert.Equal(ErrorCode.ActionNotSupported, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition!.Delivery);
        Assert.Contains("stateless mode", error.Message);
        Assert.Contains("daemon-owned", error.Suggestion);
        Assert.Empty(_posted);
    }

    [Fact]
    public void StandaloneKeyUpIsRefusedTheSameWayAsKeyDown()
    {
        var error = Assert.Throws<AdapterError>(() => Run(ActionKind.KeyUp, Combo.Parse("ctrl+a")));

        Assert.Equal(ErrorCode.ActionNotSupported, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition!.Delivery);
        Assert.Empty(_posted);
    }

    [Fact]
    public void TheRefusalRecordsThatNoRawInputWasEmitted()
    {
        var error = Assert.Throws<AdapterError>(() => Run(ActionKind.KeyDown, Combo.Parse("ctrl+a")));
        var details = JsonNode.Parse(error.Details!);

        Assert.False(details!["raw_input_emitted"]!.GetValue<bool>());
        Assert.True(details!["requires_daemon_owned_transaction"]!.GetValue<bool>());
    }

    [Fact]
    public void ARefActionOnANullHandleIsRefusedAsAnInvalidHandle()
    {
        var error = Assert.Throws<AdapterError>(() =>
            new ActionsAdapter().ExecuteAction(
                NativeHandle.Null(),
                new ActionRequest(new Action { Kind = ActionKind.Click }, true),
                Lease()));

        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
    }
}
