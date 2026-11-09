using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Input;

/// <summary>Parses the key-combo string the CLI accepts: `+`-separated modifiers then one key,
/// e.g. `ctrl+c`, `shift+tab`, `return`. A single uppercase letter with no modifiers is read as
/// Shift plus that letter, because the key name is lowercase in the platform's key table and the
/// shift is the only way to reach the uppercase glyph. The key is matched case-insensitively, so
/// the shift is not folded away with the case.</summary>
public static class Combo
{
    /// <summary>Parses a combo, adding the implicit Shift an uppercase letter carries.</summary>
    public static KeyCombo Parse(string raw)
    {
        var compact = raw.Replace(" ", "");
        var combo = ParseModifiers(compact.ToLowerInvariant());
        if (combo.Modifiers.Count == 0 && IsUppercaseLetter(compact))
        {
            combo.Modifiers.Add(Modifier.Shift);
        }

        return combo;
    }

    /// <summary>Parses a combo without the implicit Shift, so a caller can check the combo the
    /// user typed, not the combo the platform will press.</summary>
    public static KeyCombo ParseModifiers(string raw)
    {
        var parts = raw.Split('+');
        var key = parts[^1];
        if (key.Length == 0)
        {
            throw AdapterError.InvalidArgs("Empty key combo");
        }

        var combo = new KeyCombo { Key = key.ToLowerInvariant(), Modifiers = [] };
        for (var index = 0; index < parts.Length - 1; index += 1)
        {
            combo.Modifiers.Add(parts[index] switch
            {
                "meta" or "cmd" or "command" or "super" or "win" => Modifier.Meta,
                "ctrl" or "control" => Modifier.Ctrl,
                "alt" or "option" => Modifier.Alt,
                "shift" => Modifier.Shift,
                _ => throw AdapterError.InvalidArgs($"Unknown modifier: '{parts[index]}'"),
            });
        }

        return combo;
    }

    private static bool IsUppercaseLetter(string key) =>
        key.Length == 1 && char.IsAscii(key[0]) && char.IsUpper(key[0]);
}
