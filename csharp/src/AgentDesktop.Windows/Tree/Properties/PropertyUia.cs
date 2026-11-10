using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Uia;
using UiaIds = AgentDesktop.Windows.Uia.PropertyIds;

namespace AgentDesktop.Windows.Tree.Properties;

/// <summary>
/// The mapping from the walk's property vocabulary to the UI Automation property ids, and the live
/// read of one property. Kept apart from <see cref="PropertyIds"/> because the numeric ids follow
/// the automation client rather than the vocabulary, which is what A2-5 measured as build-specific.
///
/// Every id is a declared constant, so the mapping refuses the properties whose id is not declared
/// with <c>NOT_SUPPORTED</c> rather than falling through to a default. A property that cannot be
/// read is not a property that can collide with another, and the refusal happens before the
/// element is reached.
/// </summary>
internal static class PropertyUia
{
    /// <summary>
    /// The property id a vocabulary entry resolves to. No numeric literal appears here, and the
    /// refusals are the properties the walk never reads.
    /// </summary>
    internal static int ToUiaProperty(TreeProperty property) => property switch
    {
        TreeProperty.Name => UiaIds.Name,
        TreeProperty.AutomationId => UiaIds.AutomationId,
        TreeProperty.ClassName => UiaIds.ClassName,
        TreeProperty.HelpText => UiaIds.HelpText,
        TreeProperty.FullDescription => UiaIds.FullDescription,
        TreeProperty.LabeledBy => UiaIds.LabeledBy,
        TreeProperty.Value => UiaIds.ValueValue,
        TreeProperty.BoundingRectangle => UiaIds.BoundingRectangle,
        TreeProperty.IsPassword => UiaIds.IsPassword,
        TreeProperty.IsOffscreen => UiaIds.IsOffscreen,
        TreeProperty.IsEnabled => UiaIds.IsEnabled,
        TreeProperty.IsKeyboardFocusable => UiaIds.IsKeyboardFocusable,
        TreeProperty.HasKeyboardFocus => UiaIds.HasKeyboardFocus,
        TreeProperty.IsRequiredForForm => UiaIds.IsRequiredForForm,
        TreeProperty.IsDialog => UiaIds.IsDialog,
        TreeProperty.ToggleState => UiaIds.ToggleState,
        TreeProperty.ExpandCollapseState => UiaIds.ExpandCollapseState,
        TreeProperty.SelectionItemIsSelected => UiaIds.SelectionItemIsSelected,
        TreeProperty.ValueIsReadOnly => UiaIds.ValueIsReadOnly,
        TreeProperty.RangeValueIsReadOnly => UiaIds.RangeValueIsReadOnly,
        TreeProperty.SelectionCanSelectMultiple => UiaIds.SelectionCanSelectMultiple,
        TreeProperty.WindowIsModal => UiaIds.WindowIsModal,
        TreeProperty.LegacyState => UiaIds.LegacyState,
        TreeProperty.LegacyDefaultAction => UiaIds.LegacyDefaultAction,
        TreeProperty.InvokeAvailable => UiaIds.IsInvokePatternAvailable,
        TreeProperty.ToggleAvailable => UiaIds.IsTogglePatternAvailable,
        TreeProperty.ExpandCollapseAvailable => UiaIds.IsExpandCollapsePatternAvailable,
        TreeProperty.SelectionItemAvailable => UiaIds.IsSelectionItemPatternAvailable,
        TreeProperty.SelectionAvailable => UiaIds.IsSelectionPatternAvailable,
        TreeProperty.ValueAvailable => UiaIds.IsValuePatternAvailable,
        TreeProperty.RangeValueAvailable => UiaIds.IsRangeValuePatternAvailable,
        TreeProperty.ScrollAvailable => UiaIds.IsScrollPatternAvailable,
        TreeProperty.ScrollItemAvailable => UiaIds.IsScrollItemPatternAvailable,
        TreeProperty.WindowAvailable => UiaIds.IsWindowPatternAvailable,
        TreeProperty.GridItemAvailable => UiaIds.IsGridItemPatternAvailable,
        TreeProperty.TableItemAvailable => UiaIds.IsTableItemPatternAvailable,
        TreeProperty.ControlType => UiaIds.ControlType,
        TreeProperty.LocalizedControlType => UiaIds.LocalizedControlType,
        TreeProperty.AriaRole => UiaIds.AriaRole,
        TreeProperty.ProviderDescription => UiaIds.ProviderDescription,
        _ => throw AdapterError.NotSupported(
            $"property {property.AsString()} has no declared UI Automation id, so it is never read"),
    };

    /// <summary>
    /// The vocabulary entry a UI Automation property id reads as, the inverse of
    /// <see cref="ToUiaProperty"/>. An id no vocabulary entry declares answers null, so a raw id
    /// outside the mapping is skipped rather than read as a property.
    /// </summary>
    internal static TreeProperty? ToTreeProperty(int uiaProperty) => uiaProperty switch
    {
        UiaIds.Name => TreeProperty.Name,
        UiaIds.AutomationId => TreeProperty.AutomationId,
        UiaIds.ClassName => TreeProperty.ClassName,
        UiaIds.HelpText => TreeProperty.HelpText,
        UiaIds.FullDescription => TreeProperty.FullDescription,
        UiaIds.LabeledBy => TreeProperty.LabeledBy,
        UiaIds.ValueValue => TreeProperty.Value,
        UiaIds.BoundingRectangle => TreeProperty.BoundingRectangle,
        UiaIds.IsPassword => TreeProperty.IsPassword,
        UiaIds.IsOffscreen => TreeProperty.IsOffscreen,
        UiaIds.IsEnabled => TreeProperty.IsEnabled,
        UiaIds.IsKeyboardFocusable => TreeProperty.IsKeyboardFocusable,
        UiaIds.HasKeyboardFocus => TreeProperty.HasKeyboardFocus,
        UiaIds.IsRequiredForForm => TreeProperty.IsRequiredForForm,
        UiaIds.IsDialog => TreeProperty.IsDialog,
        UiaIds.ToggleState => TreeProperty.ToggleState,
        UiaIds.ExpandCollapseState => TreeProperty.ExpandCollapseState,
        UiaIds.SelectionItemIsSelected => TreeProperty.SelectionItemIsSelected,
        UiaIds.ValueIsReadOnly => TreeProperty.ValueIsReadOnly,
        UiaIds.RangeValueIsReadOnly => TreeProperty.RangeValueIsReadOnly,
        UiaIds.SelectionCanSelectMultiple => TreeProperty.SelectionCanSelectMultiple,
        UiaIds.WindowIsModal => TreeProperty.WindowIsModal,
        UiaIds.LegacyState => TreeProperty.LegacyState,
        UiaIds.LegacyDefaultAction => TreeProperty.LegacyDefaultAction,
        UiaIds.IsInvokePatternAvailable => TreeProperty.InvokeAvailable,
        UiaIds.IsTogglePatternAvailable => TreeProperty.ToggleAvailable,
        UiaIds.IsExpandCollapsePatternAvailable => TreeProperty.ExpandCollapseAvailable,
        UiaIds.IsSelectionItemPatternAvailable => TreeProperty.SelectionItemAvailable,
        UiaIds.IsSelectionPatternAvailable => TreeProperty.SelectionAvailable,
        UiaIds.IsValuePatternAvailable => TreeProperty.ValueAvailable,
        UiaIds.IsRangeValuePatternAvailable => TreeProperty.RangeValueAvailable,
        UiaIds.IsScrollPatternAvailable => TreeProperty.ScrollAvailable,
        UiaIds.IsScrollItemPatternAvailable => TreeProperty.ScrollItemAvailable,
        UiaIds.IsWindowPatternAvailable => TreeProperty.WindowAvailable,
        UiaIds.IsGridItemPatternAvailable => TreeProperty.GridItemAvailable,
        UiaIds.IsTableItemPatternAvailable => TreeProperty.TableItemAvailable,
        UiaIds.ControlType => TreeProperty.ControlType,
        UiaIds.LocalizedControlType => TreeProperty.LocalizedControlType,
        UiaIds.AriaRole => TreeProperty.AriaRole,
        UiaIds.ProviderDescription => TreeProperty.ProviderDescription,
        _ => null,
    };

    /// <summary>
    /// Reads one property live from the provider. The element's own read already classifies the
    /// variant into known, absent, and unread, so this is a projection onto the outcome shape the
    /// vocabulary consumes. The secure-field gate is not applied here: it is decided once for the
    /// whole batch by <see cref="ElementProperties.FromReads"/>, which is where the batch's
    /// <c>IsPassword</c> read arrives with the properties it gates.
    /// </summary>
    internal static PropertyOutcome ReadOne(Element element, TreeProperty property)
    {
        if (property == TreeProperty.BoundingRectangle)
        {
            return FromField(element.CurrentBoundingRectangle());
        }

        return FromField(element.CurrentProperty(ToUiaProperty(property)));
    }

    /// <summary>
    /// Reads one property from the element's cache, the set the walk's cache request filled. A
    /// property the request did not name is a programming error, not a live fetch, so it stays
    /// unread rather than answering as a provider that implements nothing.
    /// </summary>
    internal static PropertyOutcome ReadCachedOne(Element element, TreeProperty property)
    {
        if (property == TreeProperty.BoundingRectangle)
        {
            return FromField(element.BoundingRectangle());
        }

        return FromField(element.Property(ToUiaProperty(property)));
    }

    /// <summary>
    /// Projects one classified read onto the outcome shape: a known field carries its value's
    /// shape, an absent field is absent, and any other field is unread.
    /// </summary>
    internal static PropertyOutcome FromField<T>(LocatorField<T> field) where T : notnull => field.Kind switch
    {
        LocatorFieldKind.Known => FromValue(field.Value),
        LocatorFieldKind.Absent => PropertyOutcome.Absent,
        _ => PropertyOutcome.Unknown,
    };

    private static PropertyOutcome FromValue(object? value) => value switch
    {
        string text => new PropertyOutcome.Known(new PropertyValue.Text(text)),
        bool flag => new PropertyOutcome.Known(new PropertyValue.Flag(flag)),
        int number => new PropertyOutcome.Known(new PropertyValue.Number(number)),
        Rect bounds => new PropertyOutcome.Known(new PropertyValue.Bounds(bounds)),
        _ => PropertyOutcome.Unknown,
    };
}
