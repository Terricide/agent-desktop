namespace AgentDesktop.Core.Tree;

/// <summary>
/// The canonical role vocabulary every adapter emits, in the lowercase tokens a role string is
/// normalized to. A role outside this vocabulary is not a role a census can anchor, so the
/// skeleton rule refuses it. Port of <c>Role::is_canonical</c> in
/// <c>crates/core/src/tree/role.rs</c>.
/// </summary>
public static class CanonicalRoles
{
    /// <summary>The tokens <c>Role::from_token</c> maps to a named role, in the spelling every
    /// adapter emits.</summary>
    public static readonly IReadOnlySet<string> Tokens = new HashSet<string>
    {
        "alert", "alertdialog", "application", "article", "banner", "browser", "button", "cell",
        "checkbox", "colorwell", "column", "combobox", "complementary", "contentinfo", "datefield",
        "definition", "dialog", "disclosure", "dockitem", "document", "drawer", "form", "grid",
        "group", "handle", "heading", "helptag", "image", "incrementor", "layoutitem",
        "levelindicator", "link", "list", "listbox", "log", "main", "marquee", "matte", "math",
        "menu", "menubutton", "menuitem", "navigation", "note", "option", "outline", "page",
        "paragraph", "popover", "progressbar", "radiobutton", "radiogroup", "region",
        "relevanceindicator", "row", "ruler", "rulermarker", "scrollarea", "scrollbar", "search",
        "separator", "sheet", "slider", "splitter", "statictext", "status", "switch", "tab",
        "tablist", "tabpanel", "table", "textfield", "term", "timefield", "timer", "toolbar",
        "tooltip", "treeitem", "webarea", "window",
    };

    /// <summary>Whether a role string names a role the vocabulary knows. A blank role, and a role
    /// the vocabulary does not name, is not canonical, so it anchors nothing.</summary>
    public static bool IsCanonical(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        return normalized == "unknown" || Tokens.Contains(normalized);
    }

    /// <summary>The roles whose value the user changes, so a name that repeats the value is that
    /// value read twice, not a second identity field. Port of <c>is_mutable_value_role</c>.</summary>
    public static bool IsMutableValueRole(string role) =>
        role.Trim().ToLowerInvariant() switch
        {
            "combobox" or "checkbox" or "handle" or "incrementor" or "listbox"
                or "radiobutton" or "scrollbar" or "slider" or "switch" or "textfield" => true,
            _ => false,
        };
}
