namespace AgentDesktop.Cli;

/// <summary>One command the argv named, with its flags and positional values.</summary>
public sealed record ParsedCommand(string Name, Dictionary<string, string?> Flags, List<string> Positional)
{
    public string? Value(string name) => Flags.TryGetValue(name, out var value) ? value : null;

    public bool IsSet(string name) => Flags.ContainsKey(name);

    public int? Integer(string name) => int.TryParse(Value(name), out var parsed) ? parsed : null;

    public double? Real(string name) =>
        double.TryParse(Value(name), System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>The first positional value, which is the ref every ref command takes.</summary>
    public string? Ref => Positional.Count > 0 ? Positional[0] : null;

    /// <summary>The session a command runs against: the --session flag, then the environment.</summary>
    public string? Session(string? fromEnvironment) =>
        Value("session") ?? (string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment);
}

/// <summary>The argv outcome: a command, or the reason argv did not name one.</summary>
public sealed record ParseOutcome(ParsedCommand? Command, string? Error);

/// <summary>Reads argv into a command. A flag is `--name` and takes the next token as its
/// value, unless the next token is another flag, in which case the flag is set with no
/// value. A token that is not a flag is a positional value.</summary>
public static class ArgParser
{
    public static ParseOutcome Parse(string[] argv)
    {
        if (argv.Length == 0)
        {
            return new ParseOutcome(null, "No command given");
        }

        var name = argv[0];
        if (!CommandTable.Names.Contains(name))
        {
            return new ParseOutcome(null, $"'{name}' is not a command");
        }

        var flags = new Dictionary<string, string?>();
        var positional = new List<string>();
        for (var index = 1; index < argv.Length; index++)
        {
            var token = argv[index];
            if (!token.StartsWith("--"))
            {
                positional.Add(token);
                continue;
            }

            var flag = token[2..];
            if (flag.Length == 0)
            {
                return new ParseOutcome(null, "A flag name must follow --");
            }

            var next = index + 1 < argv.Length ? argv[index + 1] : null;
            if (next is not null && !next.StartsWith("--"))
            {
                flags[flag] = next;
                index++;
            }
            else
            {
                flags[flag] = null;
            }
        }

        return new ParseOutcome(new ParsedCommand(name, flags, positional), null);
    }
}
