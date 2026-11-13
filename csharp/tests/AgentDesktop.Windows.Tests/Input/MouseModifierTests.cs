using AgentDesktop.Core.Input;
using AgentDesktop.Windows.Input;
using AgentDesktop.Windows.Input.Mouse;
using AgentDesktop.Windows.Uia;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

/// <summary>Cases for the minimal modifier press and release a pointer event needs. The
/// `SendInput` seam is replaced with a recorder, so no key event reaches the desktop and the
/// exact post order is checkable: a modifier is held before the click, every held key is
/// released most-recently-pressed first, an abandoned guard never leaves a key held, and an
/// explicit release plus a later dispose posts exactly one release per key.</summary>
[Collection(AgentDesktop.Windows.Tests.KeyboardSendSinkCollection.Name)]
public sealed class MouseModifierTests : IDisposable
{
    private readonly List<KeyboardInputEvent> _posted = [];
    private readonly Action<IReadOnlyList<KeyboardInputEvent>> _originalSink;

    public MouseModifierTests()
    {
        _originalSink = KeyboardSend.PostSink;
        KeyboardSend.PostSink = events => _posted.AddRange(events);
    }

    public void Dispose() => KeyboardSend.PostSink = _originalSink;

    private List<(ushort Vk, bool Up)> Pairs() =>
        _posted.Select(e => (e.Vk, (e.Flags & KeyboardIds.KeyEventKeyUp) != 0)).ToList();

    [Fact]
    public void PressingAModifierPostsItsKeyDownBeforeAnyRelease()
    {
        var guard = MouseModifier.PressModifiers([Modifier.Ctrl]);

        Assert.Equal([(KeyboardIds.VkControl, false)], Pairs());
        guard.Dispose();
    }

    [Fact]
    public void ReleaseSweepsEveryHeldModifierMostRecentlyPressedFirst()
    {
        var guard = MouseModifier.PressModifiers([Modifier.Ctrl, Modifier.Shift]);

        guard.Release();

        Assert.Equal(
            [(KeyboardIds.VkControl, false), (KeyboardIds.VkShift, false), (KeyboardIds.VkShift, true), (KeyboardIds.VkControl, true)],
            Pairs());
    }

    [Fact]
    public void DisposingAnUnreleasedGuardStillReleasesEveryModifier()
    {
        using (MouseModifier.PressModifiers([Modifier.Alt]))
        {
            Assert.Equal([(KeyboardIds.VkMenu, false)], Pairs());
        }

        Assert.Equal(2, Pairs().Count);
        Assert.Contains((KeyboardIds.VkMenu, true), Pairs());
    }

    [Fact]
    public void ExplicitReleaseThenDisposePostsOneReleasePerKey()
    {
        var guard = MouseModifier.PressModifiers([Modifier.Meta]);

        guard.Release();
        guard.Release();
        guard.Dispose();

        Assert.Equal(1, Pairs().Count(pair => pair.Up));
    }

    [Fact]
    public void NoModifiersPressesAndReleasesNothing()
    {
        var guard = MouseModifier.PressModifiers([]);

        guard.Dispose();

        Assert.Empty(Pairs());
    }
}
