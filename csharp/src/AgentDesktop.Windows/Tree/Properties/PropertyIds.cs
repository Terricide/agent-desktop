namespace AgentDesktop.Windows.Tree.Properties;

/// <summary>
/// Static tables and classification helpers for <see cref="TreeProperty"/>.
/// </summary>
internal static class PropertyIds
{
    /// <summary>
    /// Every property whose content originates in the target and must therefore be withheld
    /// from a control reporting IsPassword. LabeledBy is excluded because it yields an element,
    /// not text; text derived from it is guarded on the label target's own IsPassword.
    /// </summary>
    public static readonly IReadOnlyList<TreeProperty> ValueBearing =
    [
        TreeProperty.Name,
        TreeProperty.HelpText,
        TreeProperty.FullDescription,
        TreeProperty.Value,
        TreeProperty.LegacyValue,
        TreeProperty.LegacyDefaultAction,
    ];

    /// <summary>
    /// The set a subtree walk reads, and therefore the set a cache request carries. Flat by
    /// measured decision (A15-11, A15-12, A16-7): a conditional split was not faster and its
    /// round trip per pattern-bearing node is unbounded on selection-dense trees.
    /// </summary>
    public static readonly IReadOnlyList<TreeProperty> WalkSet =
    [
        TreeProperty.Name,
        TreeProperty.AutomationId,
        TreeProperty.ClassName,
        TreeProperty.HelpText,
        TreeProperty.FullDescription,
        TreeProperty.LabeledBy,
        TreeProperty.Value,
        TreeProperty.LegacyValue,
        TreeProperty.BoundingRectangle,
        TreeProperty.IsPassword,
        TreeProperty.IsOffscreen,
        TreeProperty.IsEnabled,
        TreeProperty.IsControlElement,
        TreeProperty.IsContentElement,
        TreeProperty.IsKeyboardFocusable,
        TreeProperty.HasKeyboardFocus,
        TreeProperty.IsRequiredForForm,
        TreeProperty.IsDialog,
        TreeProperty.ToggleState,
        TreeProperty.ExpandCollapseState,
        TreeProperty.SelectionItemIsSelected,
        TreeProperty.ValueIsReadOnly,
        TreeProperty.RangeValueIsReadOnly,
        TreeProperty.SelectionCanSelectMultiple,
        TreeProperty.WindowIsModal,
        TreeProperty.LegacyState,
        TreeProperty.LegacyDefaultAction,
        TreeProperty.ControlType,
        TreeProperty.InvokeAvailable,
        TreeProperty.ToggleAvailable,
        TreeProperty.ExpandCollapseAvailable,
        TreeProperty.SelectionItemAvailable,
        TreeProperty.SelectionAvailable,
        TreeProperty.ValueAvailable,
        TreeProperty.RangeValueAvailable,
        TreeProperty.ScrollAvailable,
        TreeProperty.ScrollItemAvailable,
        TreeProperty.WindowAvailable,
        TreeProperty.GridItemAvailable,
        TreeProperty.TableItemAvailable,
        TreeProperty.LegacyAvailable,
        TreeProperty.LocalizedControlType,
        TreeProperty.AriaRole,
    ];

    public static bool IsValueBearing(this TreeProperty property) => ValueBearing.Contains(property);

    /// <summary>
    /// Whether the property's value is text read out of the target element, declared
    /// independently of <see cref="ValueBearing"/>. Every property returning true here must also
    /// return true from <see cref="IsValueBearing"/>.
    /// </summary>
    public static bool CarriesTargetText(this TreeProperty property) => property switch
    {
        TreeProperty.Name
            or TreeProperty.HelpText
            or TreeProperty.FullDescription
            or TreeProperty.Value
            or TreeProperty.LegacyValue
            or TreeProperty.LegacyDefaultAction => true,
        _ => false,
    };

    /// <summary>
    /// Whether the property's value is an element rather than a scalar. Element-valued
    /// properties ride the cache request but are resolved by their consumer, never through
    /// the scalar classifier, which would report an element (VT_UNKNOWN) as a failed read.
    /// </summary>
    public static bool IsElementValued(this TreeProperty property) => property == TreeProperty.LabeledBy;

    /// <summary>
    /// The availability property that must report true before this property's value means
    /// anything. Pattern-state properties return a default-looking value rather than a
    /// not-supported sentinel on elements lacking the pattern (A15-7), so the gate lives on the
    /// property and cannot be forgotten by a consumer.
    /// </summary>
    public static TreeProperty? Gate(this TreeProperty property) => property switch
    {
        TreeProperty.ToggleState => TreeProperty.ToggleAvailable,
        TreeProperty.ExpandCollapseState => TreeProperty.ExpandCollapseAvailable,
        TreeProperty.SelectionItemIsSelected => TreeProperty.SelectionItemAvailable,
        TreeProperty.ValueIsReadOnly => TreeProperty.ValueAvailable,
        TreeProperty.RangeValueIsReadOnly => TreeProperty.RangeValueAvailable,
        TreeProperty.SelectionCanSelectMultiple => TreeProperty.SelectionAvailable,
        TreeProperty.WindowIsModal => TreeProperty.WindowAvailable,
        _ => null,
    };

    /// <summary>
    /// Names the property for a structured error without exposing its value.
    /// </summary>
    public static string AsString(this TreeProperty property) => property switch
    {
        TreeProperty.Name => "Name",
        TreeProperty.AutomationId => "AutomationId",
        TreeProperty.ClassName => "ClassName",
        TreeProperty.HelpText => "HelpText",
        TreeProperty.FullDescription => "FullDescription",
        TreeProperty.LabeledBy => "LabeledBy",
        TreeProperty.Value => "Value",
        TreeProperty.LegacyValue => "LegacyIAccessibleValue",
        TreeProperty.BoundingRectangle => "BoundingRectangle",
        TreeProperty.IsPassword => "IsPassword",
        TreeProperty.IsOffscreen => "IsOffscreen",
        TreeProperty.IsEnabled => "IsEnabled",
        TreeProperty.IsControlElement => "IsControlElement",
        TreeProperty.IsContentElement => "IsContentElement",
        TreeProperty.IsKeyboardFocusable => "IsKeyboardFocusable",
        TreeProperty.HasKeyboardFocus => "HasKeyboardFocus",
        TreeProperty.IsRequiredForForm => "IsRequiredForForm",
        TreeProperty.IsDataValidForForm => "IsDataValidForForm",
        TreeProperty.IsDialog => "IsDialog",
        TreeProperty.ToggleState => "ToggleToggleState",
        TreeProperty.ExpandCollapseState => "ExpandCollapseExpandCollapseState",
        TreeProperty.SelectionItemIsSelected => "SelectionItemIsSelected",
        TreeProperty.ValueIsReadOnly => "ValueIsReadOnly",
        TreeProperty.RangeValueIsReadOnly => "RangeValueIsReadOnly",
        TreeProperty.SelectionCanSelectMultiple => "SelectionCanSelectMultiple",
        TreeProperty.WindowIsModal => "WindowIsModal",
        TreeProperty.LegacyState => "LegacyIAccessibleState",
        TreeProperty.LegacyDefaultAction => "LegacyIAccessibleDefaultAction",
        TreeProperty.InvokeAvailable => "IsInvokePatternAvailable",
        TreeProperty.ToggleAvailable => "IsTogglePatternAvailable",
        TreeProperty.ExpandCollapseAvailable => "IsExpandCollapsePatternAvailable",
        TreeProperty.SelectionItemAvailable => "IsSelectionItemPatternAvailable",
        TreeProperty.SelectionAvailable => "IsSelectionPatternAvailable",
        TreeProperty.ValueAvailable => "IsValuePatternAvailable",
        TreeProperty.RangeValueAvailable => "IsRangeValuePatternAvailable",
        TreeProperty.ScrollAvailable => "IsScrollPatternAvailable",
        TreeProperty.ScrollItemAvailable => "IsScrollItemPatternAvailable",
        TreeProperty.WindowAvailable => "IsWindowPatternAvailable",
        TreeProperty.GridItemAvailable => "IsGridItemPatternAvailable",
        TreeProperty.TableItemAvailable => "IsTableItemPatternAvailable",
        TreeProperty.LegacyAvailable => "IsLegacyIAccessiblePatternAvailable",
        TreeProperty.ProviderDescription => "ProviderDescription",
        TreeProperty.ControlType => "ControlType",
        TreeProperty.RuntimeId => "RuntimeId",
        TreeProperty.LocalizedControlType => "LocalizedControlType",
        TreeProperty.AriaRole => "AriaRole",
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, null),
    };
}
