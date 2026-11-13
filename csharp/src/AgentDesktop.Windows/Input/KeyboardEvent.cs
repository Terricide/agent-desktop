using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Input;

/// <summary>Modifier chords: press modifiers down, press and release the key, release
/// modifiers, under a release guard that sweeps any key its own bookkeeping still believes is
/// held once the sequence ends, whether it ended cleanly or was interrupted. Unlike a modifier
/// guard that always posts the keys it holds on release, this guard reads live key state first:
/// a key the guard still lists as held but that the OS no longer reports as down is not
/// re-pressed by a spurious release.</summary>
internal static class KeyboardEvent
{
    internal static readonly TimeSpan KeySettle = TimeSpan.FromMilliseconds(4);

    internal const string KeyReleaseSuggestion =
        "Inspect which modifiers may still be held before retrying; the emergency release was posted without an OS acknowledgement";

    /// <summary>The chord primitive. It carries no policy and no focus check by design: the
    /// physical action leg composes it with the policy gate and the focus verify, so the
    /// primitive stays usable by any caller that has already established those.</summary>
    internal static void SynthesizeKey(KeyCombo combo, Deadline deadline)
    {
        var resolved = KeyboardMap.ResolveKey(combo.Key);
        var modifierVks = ChordModifiers(combo, resolved);
        var guard = new KeyReleaseGuard();
        try
        {
            PressChord(guard, modifierVks, resolved.Vk, deadline);
        }
        catch (AdapterError error)
        {
            throw guard.EnrichError(error);
        }
    }

    private static void PressChord(KeyReleaseGuard guard, IReadOnlyList<ushort> modifierVks, ushort keyVk, Deadline deadline)
    {
        foreach (var vk in modifierVks)
        {
            EnsureRemaining(deadline);
            KeyboardSend.PostKeyboardInputs([KeyInput(vk, false)]);
            guard.NotePressed(vk);
        }

        EnsureRemaining(deadline);
        KeyboardSend.PostKeyboardInputs([KeyInput(keyVk, false)]);
        guard.NotePressed(keyVk);

        SleepBounded(deadline, KeySettle);

        KeyboardSend.PostKeyboardInputs([KeyInput(keyVk, true)]);
        guard.NoteReleased(keyVk);
        for (var i = modifierVks.Count - 1; i >= 0; i--)
        {
            EnsureRemaining(deadline);
            KeyboardSend.PostKeyboardInputs([KeyInput(modifierVks[i], true)]);
            guard.NoteReleased(modifierVks[i]);
        }
    }

    /// <summary>The caller's modifiers, plus any the active layout needs to produce this
    /// character, without pressing the same modifier twice.</summary>
    internal static List<ushort> ChordModifiers(KeyCombo combo, ResolvedKey resolved)
    {
        var vks = combo.Modifiers.Select(ModifierVk).ToList();
        foreach (var vk in resolved.LayoutModifierVks())
        {
            if (!vks.Contains(vk))
            {
                vks.Add(vk);
            }
        }

        return vks;
    }

    internal static ushort ModifierVk(Modifier modifier) => modifier switch
    {
        Modifier.Meta => KeyboardIds.VkLWin,
        Modifier.Ctrl => KeyboardIds.VkControl,
        Modifier.Alt => KeyboardIds.VkMenu,
        Modifier.Shift => KeyboardIds.VkShift,
        _ => KeyboardIds.VkShift,
    };

    internal static KeyboardInputEvent KeyInput(ushort vk, bool keyUp) =>
        new(vk, 0, keyUp ? KeyboardIds.KeyEventKeyUp : 0u);

    private static void EnsureRemaining(Deadline deadline)
    {
        if (deadline.IsExpired())
        {
            throw Tree.WindowRoot.DeadlineExceeded(deadline);
        }
    }

    private static void SleepBounded(Deadline deadline, TimeSpan duration)
    {
        var capped = Math.Max(0, Math.Min(deadline.RemainingMs(), (int)duration.TotalMilliseconds));
        if (capped > 0)
        {
            Thread.Sleep((int)capped);
        }

        EnsureRemaining(deadline);
    }

    /// <summary>Tracks which virtual keys this chord believes it has pressed and not yet
    /// released. Disposal sweeps whatever remains, gated per key on a live key-state read so a
    /// key already released, or released by another actor, is never re-pressed by a stale sweep.</summary>
    internal sealed class KeyReleaseGuard : IDisposable
    {
        private readonly List<ushort> _held = [];

        private readonly ReleaseState _state = new();

        internal void NotePressed(ushort vk)
        {
            _held.Add(vk);
            _state.Arm();
            _state.MarkDelivered();
        }

        internal void NoteReleased(ushort vk)
        {
            _held.Remove(vk);
            if (_held.Count == 0)
            {
                _state.Disarm();
            }

            _state.MarkDelivered();
        }

        internal AdapterError EnrichError(AdapterError error) =>
            _state.EnrichError(error, KeyReleaseSuggestion);

        public void Dispose()
        {
            for (var i = _held.Count - 1; i >= 0; i--)
            {
                if (KeyboardSend.KeyIsDown(_held[i]))
                {
                    KeyboardSend.PostKeyboardInputs([KeyInput(_held[i], true)]);
                }
            }
        }
    }
}
