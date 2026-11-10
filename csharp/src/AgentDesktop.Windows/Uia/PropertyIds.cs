namespace AgentDesktop.Windows.Uia;

/// <summary>UI Automation property ids, as UIAutomationClient.h (10.0.26100.0) declares them.</summary>
public static class PropertyIds
{
    public const int BoundingRectangle = 30001;

    public const int ControlType = 30003;

    public const int LocalizedControlType = 30004;

    public const int Name = 30005;

    public const int HasKeyboardFocus = 30008;

    public const int IsKeyboardFocusable = 30009;

    public const int IsEnabled = 30010;

    public const int AutomationId = 30011;

    public const int ClassName = 30012;

    public const int HelpText = 30013;

    public const int IsPassword = 30019;

    /// <summary>The label relation. It yields an element, not text, so it is not a
    /// value-bearing property: there is no content on this element to withhold. The text
    /// derived from it is guarded on the target side, where the secure gate lives.</summary>
    public const int LabeledBy = 30018;

    public const int IsOffscreen = 30022;

    public const int FrameworkId = 30024;

    public const int IsRequiredForForm = 30025;

    public const int IsExpandCollapsePatternAvailable = 30028;

    public const int IsGridItemPatternAvailable = 30029;

    public const int IsInvokePatternAvailable = 30031;

    public const int IsRangeValuePatternAvailable = 30033;

    public const int IsScrollPatternAvailable = 30034;

    public const int IsScrollItemPatternAvailable = 30035;

    public const int IsSelectionItemPatternAvailable = 30036;

    public const int IsSelectionPatternAvailable = 30037;

    public const int IsTableItemPatternAvailable = 30039;

    public const int IsTogglePatternAvailable = 30041;

    public const int IsValuePatternAvailable = 30043;

    public const int IsWindowPatternAvailable = 30044;

    public const int ValueValue = 30045;

    public const int ValueIsReadOnly = 30046;

    public const int RangeValueIsReadOnly = 30048;

    public const int SelectionCanSelectMultiple = 30060;

    public const int ExpandCollapseState = 30070;

    public const int WindowIsModal = 30077;

    public const int SelectionItemIsSelected = 30079;

    public const int ToggleState = 30086;

    public const int LegacyState = 30096;

    public const int LegacyDefaultAction = 30100;

    public const int AriaRole = 30101;

    public const int ProviderDescription = 30107;

    public const int FullDescription = 30159;

    public const int IsDialog = 30174;
}