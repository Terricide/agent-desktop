namespace AgentDesktop.Windows.Tree.Properties;

/// <summary>
/// The properties the walk reads, named independently of the UI Automation numbering.
/// UIA property ids are build-specific (A2-5), so no numeric table lives here; every id is
/// resolved through the generated constants in the UIA mapping.
/// Pattern-derived state is read as ordinary properties (ToggleState, ExpandCollapseState,
/// SelectionItem.IsSelected, Value.IsReadOnly, Selection.CanSelectMultiple), so it arrives in the
/// same batch as everything else and costs no QueryInterface per node per pattern.
/// </summary>
internal enum TreeProperty
{
    Name,
    AutomationId,
    ClassName,
    HelpText,
    FullDescription,
    LabeledBy,
    Value,
    LegacyValue,
    BoundingRectangle,
    IsPassword,
    IsOffscreen,
    IsEnabled,
    IsControlElement,
    IsContentElement,
    IsKeyboardFocusable,
    HasKeyboardFocus,
    IsRequiredForForm,
    IsDataValidForForm,
    IsDialog,
    ToggleState,
    ExpandCollapseState,
    SelectionItemIsSelected,
    ValueIsReadOnly,
    RangeValueIsReadOnly,
    SelectionCanSelectMultiple,
    WindowIsModal,
    LegacyState,
    LegacyDefaultAction,
    InvokeAvailable,
    ToggleAvailable,
    ExpandCollapseAvailable,
    SelectionItemAvailable,
    SelectionAvailable,
    ValueAvailable,
    RangeValueAvailable,
    ScrollAvailable,
    ScrollItemAvailable,
    WindowAvailable,
    GridItemAvailable,
    TableItemAvailable,
    LegacyAvailable,
    ProviderDescription,
    ControlType,
    RuntimeId,
    LocalizedControlType,
    AriaRole,
}
