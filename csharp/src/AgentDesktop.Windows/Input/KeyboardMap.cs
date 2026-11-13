using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Input;

/// <summary>A key name resolved to the virtual key that produces it, together with the
/// modifiers the active layout requires to produce it. The layout half is what `VkKeyScanW`
/// reports and a caller cannot know: on AZERTY the digit row needs Shift, so pressing the
/// bare virtual key for `5` yields the unshifted glyph instead.</summary>
internal readonly record struct ResolvedKey(ushort Vk, bool LayoutShift, bool LayoutControl, bool LayoutAlt)
{
    internal static ResolvedKey Bare(ushort vk) => new(vk, false, false, false);

    internal List<ushort> LayoutModifierVks()
    {
        var vks = new List<ushort>();
        if (LayoutShift)
        {
            vks.Add(KeyboardIds.VkShift);
        }

        if (LayoutControl)
        {
            vks.Add(KeyboardIds.VkControl);
        }

        if (LayoutAlt)
        {
            vks.Add(KeyboardIds.VkMenu);
        }

        return vks;
    }
}

/// <summary>Key-name to Windows virtual-key resolution. Key names are a shared contract with
/// the other platforms: every name the macOS map accepts resolves here too, even though what
/// a combo built from those names does is not portable. `delete`/`backspace` and
/// `forwarddelete` keep the split between a single Backspace-shaped key and the separate
/// forward-delete key, mapped onto Windows' own two distinct keys rather than collapsing them.
/// Single-character names resolve through `VkKeyScanW` so the physical key pressed matches the
/// active keyboard layout, falling back to the ASCII-derived virtual key when the layout has
/// no key that produces the requested character at all.</summary>
internal static class KeyboardMap
{
    internal static ResolvedKey ResolveKey(string key)
    {
        if (NamedKeyVk(key) is { } named)
        {
            return ResolvedKey.Bare(named);
        }

        if (CharacterKey(key) is { } character)
        {
            return ResolveCharacter(character);
        }

        throw UnknownKeyError(key);
    }

    internal static ushort? NamedKeyVk(string key) => key switch
    {
        "return" or "enter" => KeyboardIds.VkReturn,
        "escape" or "esc" => KeyboardIds.VkEscape,
        "tab" => KeyboardIds.VkTab,
        "space" => KeyboardIds.VkSpace,
        "delete" or "backspace" => KeyboardIds.VkBack,
        "forwarddelete" => KeyboardIds.VkDelete,
        "home" => KeyboardIds.VkHome,
        "end" => KeyboardIds.VkEnd,
        "pageup" => KeyboardIds.VkPrior,
        "pagedown" => KeyboardIds.VkNext,
        "left" => KeyboardIds.VkLeft,
        "right" => KeyboardIds.VkRight,
        "down" => KeyboardIds.VkDown,
        "up" => KeyboardIds.VkUp,
        "cmd" or "command" => KeyboardIds.VkLWin,
        "shift" => KeyboardIds.VkShift,
        "alt" or "option" => KeyboardIds.VkMenu,
        "ctrl" or "control" => KeyboardIds.VkControl,
        "f1" => KeyboardIds.VkF1,
        "f2" => (ushort)(KeyboardIds.VkF1 + 1),
        "f3" => (ushort)(KeyboardIds.VkF1 + 2),
        "f4" => (ushort)(KeyboardIds.VkF1 + 3),
        "f5" => (ushort)(KeyboardIds.VkF1 + 4),
        "f6" => (ushort)(KeyboardIds.VkF1 + 5),
        "f7" => (ushort)(KeyboardIds.VkF1 + 6),
        "f8" => (ushort)(KeyboardIds.VkF1 + 7),
        "f9" => (ushort)(KeyboardIds.VkF1 + 8),
        "f10" => (ushort)(KeyboardIds.VkF1 + 9),
        "f11" => (ushort)(KeyboardIds.VkF1 + 10),
        "f12" => KeyboardIds.VkF12,
        _ => null,
    };

    internal static ResolvedKey ResolveCharacter(char character) =>
        VkKeyScan(character) is { } scan
            ? ResolvedFromScan(scan)
            : ResolvedKey.Bare((ushort)char.ToUpperInvariant(character));

    /// <summary>`VkKeyScanW` packs the virtual key in the low byte and the modifiers the
    /// layout needs in the high byte. Keeping only the low byte presses the key without the
    /// shift the layout requires, which produces the wrong character rather than a failure.</summary>
    internal static ResolvedKey ResolvedFromScan(short scan)
    {
        var state = (byte)((scan >> 8) & 0xFF);
        return new ResolvedKey(
            (ushort)(scan & 0x00FF),
            (state & KeyboardIds.LayoutShift) != 0,
            (state & KeyboardIds.LayoutControl) != 0,
            (state & KeyboardIds.LayoutAlt) != 0);
    }

    private static char? CharacterKey(string key)
    {
        if (key.Length != 1)
        {
            return null;
        }

        var character = key[0];
        return char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) ? character : null;
    }

    private static short? VkKeyScan(char character)
    {
        var result = VkKeyScanW(character);
        return result == -1 ? null : result;
    }

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    private static AdapterError UnknownKeyError(string key) =>
        AdapterError.InvalidArgs($"Unknown key: '{key}'")
            .WithSuggestion(
                "Valid keys: a-z, 0-9, return, escape, tab, space, delete, forwarddelete, home, "
                + "end, pageup, pagedown, left, right, up, down, cmd, shift, alt, ctrl, f1-f12");
}
