namespace AgentDesktop.Core.Tree;

/// <summary>A role as a query names it. A query role is folded to the canonical role before
/// it is compared, so a caller can name a role by the shorthand the web platform uses
/// (`img`, `radio`, `textbox`) and reach the role this product reports.</summary>
public static class RoleQuery
{
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["gridcell"] = "cell",
        ["img"] = "image",
        ["radio"] = "radiobutton",
        ["searchbox"] = "textfield",
        ["searchfield"] = "textfield",
        ["textarea"] = "textfield",
        ["textbox"] = "textfield",
        ["spinbutton"] = "incrementor",
        ["togglebutton"] = "button",
        ["tree"] = "outline",
    };

    public static string Normalize(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        return Aliases.TryGetValue(normalized, out var canonical) ? canonical : normalized;
    }
}
