namespace AgentDesktop.Core.Tree;

/// <summary>The state tokens a live read reports in its states list, and the only names a
/// state predicate is accepted in. A token outside this list is refused before the
/// application is read, so a query that asks for a state the product does not report fails
/// with the vocabulary named rather than returning no matches.</summary>
public static class TreeStates
{
    public const string Hidden = "hidden";

    public static readonly IReadOnlyList<string> Vocabulary = new[]
    {
        "focused", "disabled", "secure", "expanded", "checked", "selected", "hidden", "busy",
        "modal", "required", "indeterminate", "pressed", "readonly", "offscreen", "invalid",
        "multiselectable", "haspopup",
    };

    public static bool Has(List<string> states, string wanted) => states.Contains(wanted);

    public static bool IsKnownToken(string token) => Vocabulary.Contains(token);
}
