using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>The canonical role for a UI Automation ControlType id, refined by the pattern
/// availability in the same read set. Mirrors crates/windows/src/tree/roles.rs arm for arm.
/// A failed ControlType read and an id this map does not name both resolve to unknown.</summary>
internal static class Roles
{
    internal const string Unknown = "unknown";

    internal static string Resolve(NodeProperties properties) =>
        properties.Number(PropertyIds.ControlType) is { } controlType
            ? ForControlType(controlType, properties)
            : Unknown;

    internal static string ForControlType(int controlType, NodeProperties properties) => controlType switch
    {
        ControlTypeIds.Button => ButtonRole(properties),
        ControlTypeIds.Calendar => "group",
        ControlTypeIds.CheckBox => "checkbox",
        ControlTypeIds.ComboBox => "combobox",
        ControlTypeIds.Edit => "textfield",
        ControlTypeIds.Hyperlink => "link",
        ControlTypeIds.Image => "image",
        ControlTypeIds.ListItem => "option",
        ControlTypeIds.List => properties.IsTrue(PropertyIds.IsSelectionPatternAvailable) ? "listbox" : "list",
        ControlTypeIds.Menu => "menu",
        ControlTypeIds.MenuBar => "menu",
        ControlTypeIds.MenuItem => "menuitem",
        ControlTypeIds.ProgressBar => "progressbar",
        ControlTypeIds.RadioButton => "radiobutton",
        ControlTypeIds.ScrollBar => "scrollbar",
        ControlTypeIds.Slider => "slider",
        ControlTypeIds.Spinner => "incrementor",
        ControlTypeIds.StatusBar => "status",
        ControlTypeIds.Tab => "tablist",
        ControlTypeIds.TabItem => "tab",
        ControlTypeIds.Text => "statictext",
        ControlTypeIds.ToolBar => "toolbar",
        ControlTypeIds.ToolTip => "tooltip",
        ControlTypeIds.Tree => "outline",
        ControlTypeIds.TreeItem => "treeitem",
        ControlTypeIds.Custom => IsGridAddressable(properties) ? "cell" : Unknown,
        ControlTypeIds.Group => "group",
        ControlTypeIds.Thumb => "handle",
        ControlTypeIds.DataGrid => "grid",
        ControlTypeIds.DataItem => IsGridAddressable(properties) ? "cell" : "row",
        ControlTypeIds.Document => IsEditableDocument(properties) ? "textfield" : "document",
        ControlTypeIds.SplitButton => "menubutton",
        ControlTypeIds.Window => "window",
        ControlTypeIds.Pane => IsDialogPane(properties) ? "dialog" : "group",
        ControlTypeIds.Header => "group",
        ControlTypeIds.HeaderItem => "column",
        ControlTypeIds.Table => "table",
        ControlTypeIds.TitleBar => "group",
        ControlTypeIds.Separator => "separator",
        ControlTypeIds.SemanticZoom => "group",
        ControlTypeIds.AppBar => "toolbar",
        _ => Unknown,
    };

    /// <summary>Toggle is the stronger claim about a button's nature, so it wins over
    /// ExpandCollapse when a provider advertises both.</summary>
    private static string ButtonRole(NodeProperties properties)
    {
        if (properties.IsTrue(PropertyIds.IsTogglePatternAvailable))
        {
            return "switch";
        }

        return properties.IsTrue(PropertyIds.IsExpandCollapsePatternAvailable) ? "menubutton" : "button";
    }

    /// <summary>An item addressable by row and column is a cell. WPF's DataGrid reports its
    /// cells as Custom with these patterns (A16-10).</summary>
    private static bool IsGridAddressable(NodeProperties properties) =>
        properties.IsTrue(PropertyIds.IsGridItemPatternAvailable)
        || properties.IsTrue(PropertyIds.IsTableItemPatternAvailable);

    private static bool IsEditableDocument(NodeProperties properties) =>
        properties.IsTrue(PropertyIds.IsValuePatternAvailable)
        && properties.GatedBool(PropertyIds.ValueIsReadOnly) == false;

    private static bool IsDialogPane(NodeProperties properties) =>
        properties.IsTrue(PropertyIds.IsWindowPatternAvailable) || properties.IsTrue(PropertyIds.IsDialog);
}
