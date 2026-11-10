using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Locator;

/// <summary>A state a candidate element must be in, with the value it must have. `focused`
/// requires the state to be present; `focused=false` requires it to be absent. A token
/// outside the state vocabulary is refused before the application is read, so a query that
/// asks for a state the product does not report fails with the vocabulary named rather than
/// returning no matches.</summary>
public sealed record StatePredicate
{
    public string Token { get; init; } = "";
    public bool? Expected { get; init; }

    public bool Matches(List<string> states) =>
        Expected ?? true ? states.Contains(Token) : !states.Contains(Token);
}

/// <summary>The result of parsing one state flag: the predicate, or the reason the flag was
/// refused. A flag is `token`, `token=true`, or `token=false`; a value that is not true, 1,
/// false, or 0 is refused, and a token the vocabulary does not carry is refused with the
/// vocabulary named.</summary>
public static class StatePredicateParser
{
    public static StateParseOutcome Parse(string raw)
    {
        var token = raw;
        bool? expected = null;
        var split = raw.Split('=', 2);
        if (split.Length > 1)
        {
            token = split[0];
            expected = split[1] switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => null,
            };
            if (expected is null)
            {
                return new StateParseOutcome(null, $"Invalid state flag value '{split[1]}'");
            }
        }

        var normalized = token.Trim().ToLowerInvariant();
        if (!TreeStates.IsKnownToken(normalized))
        {
            return new StateParseOutcome(null, $"Unknown state token '{normalized}'");
        }

        return new StateParseOutcome(new StatePredicate { Token = normalized, Expected = expected }, null);
    }
}

/// <summary>The outcome of parsing a state flag: the predicate, or the refusal.</summary>
public sealed record StateParseOutcome(StatePredicate? Predicate, string? Refusal);
