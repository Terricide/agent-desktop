using AgentDesktop.Core.Input;

namespace AgentDesktop.Windows.Input;

/// <summary>Windows-dangerous key combos. The core default blocks nothing; the Windows adapter
/// supplies this list through its combo hook. The list is reasoned from Windows semantics, not
/// translated from macOS's `cmd+q`-shaped set, which has no Windows meaning. One question decides
/// membership: does the shortcut take the foreground away from the run, or change the session out
/// from under it? `alt+f4` closes the active window, `win+l` locks the session, `win+d` shows the
/// desktop, and `alt+tab` steals the foreground mid-run. `win+r` (Run), `win+x` (Quick Link),
/// `win+e` (File Explorer) and `ctrl+shift+esc` (Task Manager) each raise a shell surface over the
/// target and answer that same question the same way, so they are listed for the same reason rather
/// than left out because they open something rather than close it. Every modifier order, key-name
/// alias, and modifier superset of a blocked shortcut is caught by canonicalizing before comparison.
/// The superset rule is this list's own, because `alt+shift+tab` is as dangerous as the `alt+tab` it
/// extends. `ctrl+alt+delete` is deliberately absent: it is the Secure Attention Sequence, which
/// `SendInput` cannot synthesize at all, so listing it here would advertise a guard this adapter does
/// not provide. The calling agent can still send any of these with `--force`, which the CLI enforces.</summary>
internal static class BlockedCombo
{
    private static readonly string[] Blocked =
    [
        "alt+f4",
        "win+l",
        "win+d",
        "alt+tab",
        "win+r",
        "win+x",
        "win+e",
        "ctrl+shift+esc",
    ];

    /// <summary>Blocks a combo whose key matches a listed shortcut and whose modifiers are a superset
    /// of that shortcut's. Exact equality is not enough: adding a modifier to a dangerous shortcut
    /// usually produces another dangerous shortcut rather than a harmless one. `alt+shift+tab` is the
    /// reverse task switcher and steals the foreground exactly as `alt+tab` does, but it is a different
    /// string, so an equality check waves it through. Superset matching over-blocks a few combinations
    /// nobody uses, such as `ctrl+alt+f4`, and that trade is deliberate: a wrongly blocked combo costs
    /// the caller one `--force`, while a wrongly allowed one moves input to another window in the middle
    /// of a run.</summary>
    internal static bool IsBlocked(KeyCombo combo)
    {
        var (modifiers, key) = CanonicalParts(ComboToString(combo));
        return Blocked.Any(entry =>
        {
            var (blockedModifiers, blockedKey) = CanonicalParts(entry);
            return blockedKey == key && blockedModifiers.All(needed => modifiers.Contains(needed));
        });
    }

    internal static string ComboToString(KeyCombo combo)
    {
        var parts = combo.Modifiers.Select(ModifierName).ToList();
        parts.Add(combo.Key);
        return string.Join("+", parts);
    }

    private static string ModifierName(Modifier modifier) => modifier switch
    {
        Modifier.Meta => "win",
        Modifier.Ctrl => "ctrl",
        Modifier.Alt => "alt",
        Modifier.Shift => "shift",
        _ => "win",
    };

    /// <summary>Canonicalizes a `mod+...+key` string for safety comparison and returns its parts:
    /// modifier names normalized, sorted and de-duplicated so order and spelling cannot evade a match,
    /// and the single key folded to one spelling per physical key. `meta`/`cmd` fold to `win` because
    /// the agent may name the Windows key either way even though the shortcut itself only exists on
    /// Windows. The parts stay separate rather than joined so the comparison can ask about modifiers
    /// and key independently, which is what superset matching needs.</summary>
    internal static (IReadOnlyList<string> Modifiers, string Key) CanonicalParts(string raw)
    {
        var lower = raw.ToLowerInvariant();
        var modifiers = new List<string>();
        var key = "";
        foreach (var part in lower.Split('+'))
        {
            switch (part)
            {
                case "win" or "meta" or "cmd" or "super":
                    modifiers.Add("win");
                    break;
                case "ctrl" or "control":
                    modifiers.Add("ctrl");
                    break;
                case "alt" or "option":
                    modifiers.Add("alt");
                    break;
                case "shift":
                    modifiers.Add("shift");
                    break;
                default:
                    key = CanonicalKey(part);
                    break;
            }
        }

        var sorted = modifiers.Distinct().OrderBy(modifier => modifier, StringComparer.Ordinal).ToList();
        return (sorted, key);
    }

    internal static string CanonicalKey(string key) => key switch
    {
        "escape" or "esc" => "esc",
        "backspace" or "delete" => "delete",
        "enter" or "return" => "return",
        _ => key,
    };
}
