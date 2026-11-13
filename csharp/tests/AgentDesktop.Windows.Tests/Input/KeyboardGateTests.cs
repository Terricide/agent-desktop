using System.Runtime.InteropServices;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Actions;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.Uia;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

/// <summary>Verifies the gates a physical key sequence must pass before it emits anything, and
/// the release guard that ends a sequence with no key held. The `SendInput` seam is replaced with
/// a recorder, so no real key event reaches the desktop.</summary>
[Collection(AgentDesktop.Windows.Tests.KeyboardSendSinkCollection.Name)]
public sealed class KeyboardGateTests
{
    [Fact]
    public void SemanticPolicyIsRefusedBeforeAnyKeyEventIsEmitted()
    {
        var error = Assert.Throws<AdapterError>(() => KeyboardLeg.EnsureKeyboardPolicy(InteractionPolicy.Semantic));

        Assert.Equal(ErrorCode.PolicyDenied, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition!.Delivery);
    }

    [Fact]
    public void PhysicalPolicyPassesTheKeyboardPolicyGate()
    {
        KeyboardLeg.EnsureKeyboardPolicy(InteractionPolicy.Physical);
    }

    [Fact]
    public void TargetAtHigherIntegrityThanTheCallerIsRefused()
    {
        Assert.False(Elevation.IntegrityPermitsInput(0x2000, 0x3000));
        Assert.True(Elevation.IntegrityPermitsInput(0x2000, 0x2000));
        Assert.True(Elevation.IntegrityPermitsInput(0x3000, 0x2000));
    }

    [Fact]
    public void ATargetWhoseIntegrityCannotBeReadProceedsBestEffort()
    {
        Elevation.EvaluateIntegrityGate(0x2000, null);
        Elevation.EvaluateIntegrityGate(null, 0x3000);
    }

    [Fact]
    public void AReadableHigherTargetIsRefusedByTheSameGate()
    {
        var error = Assert.Throws<AdapterError>(() => Elevation.EvaluateIntegrityGate(0x2000, 0x3000));

        Assert.Equal(ErrorCode.PermDenied, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition!.Delivery);
    }

    [Fact]
    public void LastSubAuthorityOfASidIsTheMandatoryLabelRid()
    {
        var sid = BuildSid([0x2000]);
        try
        {
            Assert.Equal(0x2000u, Elevation.SidLastSubAuthority(sid));
        }
        finally
        {
            Marshal.FreeHGlobal(sid);
        }

        Assert.Null(Elevation.SidLastSubAuthority(IntPtr.Zero));
    }

    [Fact]
    public void ASidWithNoSubAuthorityHasNoIntegrityRid()
    {
        var sid = BuildSid([]);
        try
        {
            Assert.Null(Elevation.SidLastSubAuthority(sid));
        }
        finally
        {
            Marshal.FreeHGlobal(sid);
        }
    }

    [Fact]
    public void ReleaseGuardSweepsAKeyItBelievesIsHeldWhenTheOsReportsItDown()
    {
        var (post, keyState) = (KeyboardSend.PostSink, KeyboardSend.KeyStateSink);
        var posted = new List<KeyboardInputEvent>();
        KeyboardSend.PostSink = posted.AddRange;
        KeyboardSend.KeyStateSink = vk => vk == 0x11;

        var guard = new KeyboardEvent.KeyReleaseGuard();
        guard.NotePressed(0x11);
        guard.NotePressed(0x12);
        guard.Dispose();

        Assert.Contains(posted, e => e.Vk == 0x11 && (e.Flags & KeyboardIds.KeyEventKeyUp) != 0);
        Assert.DoesNotContain(posted, e => e.Vk == 0x12);

        (KeyboardSend.PostSink, KeyboardSend.KeyStateSink) = (post, keyState);
    }

    [Fact]
    public void ReleaseGuardReleasesNothingWhenTheOsAlreadyReleasedEveryKey()
    {
        var (post, keyState) = (KeyboardSend.PostSink, KeyboardSend.KeyStateSink);
        var posted = new List<KeyboardInputEvent>();
        KeyboardSend.PostSink = posted.AddRange;
        KeyboardSend.KeyStateSink = _ => false;

        var guard = new KeyboardEvent.KeyReleaseGuard();
        guard.NotePressed(0x11);
        guard.Dispose();

        Assert.Empty(posted);

        (KeyboardSend.PostSink, KeyboardSend.KeyStateSink) = (post, keyState);
    }

    /// <summary>A chord interrupted after a modifier was pressed must both carry the committed
    /// evidence in the error it raises and end with that modifier released. The deadline is driven
    /// by hand here: the guard is what owns the sweep, and the sweep is what the contract is about.</summary>
    [Fact]
    public void AChordInterruptedAfterAModifierWasPressedSweepsThatModifier()
    {
        var (post, keyState) = (KeyboardSend.PostSink, KeyboardSend.KeyStateSink);
        var posted = new List<KeyboardInputEvent>();
        KeyboardSend.PostSink = posted.AddRange;
        KeyboardSend.KeyStateSink = _ => true;

        var guard = new KeyboardEvent.KeyReleaseGuard();
        guard.NotePressed(0x11);

        var error = guard.EnrichError(new AdapterError(ErrorCode.Timeout, "Deadline exceeded"));
        Assert.Equal(Delivery.DeliveredUnverified, error.Disposition!.Delivery);

        guard.Dispose();
        Assert.Contains(posted, e => e.Vk == 0x11 && (e.Flags & KeyboardIds.KeyEventKeyUp) != 0);

        (KeyboardSend.PostSink, KeyboardSend.KeyStateSink) = (post, keyState);
    }

    private static IntPtr BuildSid(uint[] subAuthorities)
    {
        var buffer = Marshal.AllocHGlobal(8 + subAuthorities.Length * 4);
        Marshal.WriteByte(buffer, 1);
        Marshal.WriteByte(buffer, 1, (byte)subAuthorities.Length);
        for (var i = 0; i < 6; i += 1)
        {
            Marshal.WriteByte(buffer, 2 + i, 0);
        }

        for (var i = 0; i < subAuthorities.Length; i += 1)
        {
            Marshal.WriteInt32(buffer, 8 + i * 4, (int)subAuthorities[i]);
        }

        return buffer;
    }
}
