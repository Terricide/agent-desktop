namespace AgentDesktop.Core.Tree;

/// <summary>The state tokens a live read reports in its states list, and the only names a
/// state predicate is accepted in. A token outside this list is refused before the
/// application is read, so a query that asks for a state the product does not report fails
/// with the vocabulary named rather than returning no matches.</summary>
public static class TreeStates
{
    public const string Focused = "focused";

    public const string Disabled = "disabled";

    public const string Secure = "secure";

    public const string Expanded = "expanded";

    public const string Checked = "checked";

    public const string Selected = "selected";

    public const string Hidden = "hidden";

    public const string Busy = "busy";

    public const string Modal = "modal";

    public const string Required = "required";

    public const string Indeterminate = "indeterminate";

    public const string Pressed = "pressed";

    public const string Readonly = "readonly";

    public const string Offscreen = "offscreen";

    public const string Invalid = "invalid";

    public const string Multiselectable = "multiselectable";

    public const string Haspopup = "haspopup";

    public static readonly IReadOnlyList<string> Vocabulary = new[]
    {
        "focused", "disabled", "secure", "expanded", "checked", "selected", "hidden", "busy",
        "modal", "required", "indeterminate", "pressed", "readonly", "offscreen", "invalid",
        "multiselectable", "haspopup",
    };

    public static bool Has(List<string> states, string wanted) => states.Contains(wanted);

    public static bool IsKnownToken(string token) => Vocabulary.Contains(token);
}
