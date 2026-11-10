using System;
using System.Collections.Generic;
using System.Linq;
using AgentDesktop.Core.Input;

namespace AgentDesktop.Windows.Input.Mouse;

/// <summary>
/// Holds modifier virtual keys down around a mouse click or wheel sequence.
/// This is the minimal press/release a pointer event needs. It posts through
/// <c>KeyboardSend.PostKeyboardInputs</c>, the single <c>SendInput</c> keyboard seam, so any change to how a
/// key event is framed (scan codes, extended-key flags, injection tag) applies to modifiers held around a click
/// exactly as it applies to chords.
/// </summary>
internal static class MouseModifier
{
    /// <summary>
    /// Posts a key-down for each modifier and returns a guard that posts the matching releases on
    /// <see cref="ModifierGuard.Release"/> or <see cref="ModifierGuard.Dispose"/>.
    /// </summary>
    internal static ModifierGuard PressModifiers(IReadOnlyList<Modifier> modifiers)
    {
        var held = modifiers.Select(KeyboardEvent.ModifierVk).ToArray();
        foreach (var vk in held)
        {
            KeyboardSend.PostKeyboardInputs(new[] { KeyboardEvent.KeyInput(vk, false) });
        }
        return new ModifierGuard(held);
    }
}

/// <summary>
/// Releases the held modifiers in reverse order. Release is idempotent, and disposing the guard always posts the
/// release, so an aborted sequence cannot leave a modifier held. Best-effort, like every other physical post: the
/// release is sent, never verified.
/// </summary>
internal sealed class ModifierGuard : IDisposable
{
    private readonly ushort[] held;
    private bool released;

    internal ModifierGuard(ushort[] held)
    {
        this.held = held;
    }

    internal void Release()
    {
        if (released)
        {
            return;
        }
        released = true;
        for (var i = held.Length - 1; i >= 0; i--)
        {
            KeyboardSend.PostKeyboardInputs(new[] { KeyboardEvent.KeyInput(held[i], true) });
        }
    }

    public void Dispose()
    {
        Release();
    }
}
