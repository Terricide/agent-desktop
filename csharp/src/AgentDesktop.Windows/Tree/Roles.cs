using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>The canonical role for a UI Automation ControlType id, refined by the pattern
/// availability in the same read set. Mirrors crates/windows/src/tree/roles.rs arm for arm.
/// A failed ControlType read and an id this map does not name both resolve to unknown.</summary>
internal static class Roles
{
    internal const string Unknown = "unknown";

    internal static string Resolve(ElementProperties properties) =>
        properties.Number(TreeProperty.ControlType) is { } controlType
            ? ForControlType(controlType, properties)
            : Unknown;

    internal static string ForControlType(int controlType, ElementProperties properties) => controlType switch
    {
        ControlTypeIds.Button => ButtonRole(properties),
        ControlTypeIds.Calendar => "group",
        ControlTypeIds.CheckBox => "checkbox",
        ControlTypeIds.ComboBox => "combobox",
        ControlTypeIds.Edit => "textfield",
        ControlTypeIds.Hyperlink => "link",
        ControlTypeIds.Image => "image",
        ControlTypeIds.ListItem => "option",
        ControlTypeIds.List => properties.IsTrue(TreeProperty.SelectionAvailable) ? "listbox" : "list",
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
    private static string ButtonRole(ElementProperties properties)
    {
        if (properties.IsTrue(TreeProperty.ToggleAvailable))
        {
            return "switch";
        }

        return properties.IsTrue(TreeProperty.ExpandCollapseAvailable) ? "menubutton" : "button";
    }

    /// <summary>An item addressable by row and column is a cell. WPF's DataGrid reports its
    /// cells as Custom with these patterns (A16-10).</summary>
    private static bool IsGridAddressable(ElementProperties properties) =>
        properties.IsTrue(TreeProperty.GridItemAvailable)
        || properties.IsTrue(TreeProperty.TableItemAvailable);

    private static bool IsEditableDocument(ElementProperties properties) =>
        properties.IsTrue(TreeProperty.ValueAvailable)
        && properties.GatedBool(TreeProperty.ValueIsReadOnly) == false;

    private static bool IsDialogPane(ElementProperties properties) =>
        properties.IsTrue(TreeProperty.WindowAvailable) || properties.IsTrue(TreeProperty.IsDialog);
}
