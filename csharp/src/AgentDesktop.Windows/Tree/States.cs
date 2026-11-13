using AgentDesktop.Core.Locator;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// Resolves the state vocabulary for a node from its read set and resolved role.
/// </summary>
internal static class States
{
    /// <summary>
    /// <c>STATE_SYSTEM_HASPOPUP</c>, the MSAA bit measured (A15-6) on a menu item alongside
    /// <c>STATE_SYSTEM_FOCUSABLE</c>. Neither <c>haspopup</c> nor <c>busy</c> has a UI Automation
    /// property of its own, so this legacy bitmask is their only source on Windows.
    /// </summary>
    private const int StateSystemHaspopup = 0x4000_0000;

    /// <summary>
    /// <c>STATE_SYSTEM_BUSY</c>, the bit measured (A15-6) alone on a plain text control.
    /// </summary>
    private const int StateSystemBusy = 0x0000_0800;

    /// <summary>
    /// <c>STATE_SYSTEM_PRESSED</c>, the MSAA bit for a button in pressed state.
    /// </summary>
    private const int StateSystemPressed = 0x0000_0008;

    private const int ToggleStateOn = 1;
    private const int ToggleStateIndeterminate = 2;

    private const int ExpandCollapseStateExpanded = 1;

    private const string DisabledToken = "disabled";
    private const string SecureToken = "secure";
    private const string OffscreenToken = "offscreen";
    private const string FocusedToken = "focused";
    private const string RequiredToken = "required";
    private const string CheckedToken = "checked";
    private const string IndeterminateToken = "indeterminate";
    private const string ExpandedToken = "expanded";
    private const string SelectedToken = "selected";
    private const string ReadonlyToken = "readonly";
    private const string MultiselectableToken = "multiselectable";
    private const string ModalToken = "modal";
    private const string PressedToken = "pressed";
    private const string HaspopupToken = "haspopup";
    private const string BusyToken = "busy";

    /// <summary>
    /// Ungated sources the walk always requests for every node. If every one of these reads back
    /// <c>Unknown</c>, nothing about the element was read; that is the shape
    /// <see cref="ElementProperties"/> produces for a property no entry was ever supplied for.
    /// </summary>
    private static readonly TreeProperty[] ReadHealthProbes =
    [
        TreeProperty.IsEnabled,
        TreeProperty.IsOffscreen,
        TreeProperty.HasKeyboardFocus,
        TreeProperty.IsRequiredForForm,
        TreeProperty.IsPassword,
        TreeProperty.LegacyState,
    ];

    /// <summary>
    /// Resolves the state vocabulary from the read set and the resolved role.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>invalid</c> is deliberately unproduced. Microsoft's ARIA state table gives
    /// <c>IsDataValidForForm</c> as the source, and the first implementation read it that way. Run
    /// against four real targets it emitted <c>invalid</c> on every node of every one of them (26 of 26
    /// on Notepad, 113 of 113 on Explorer), on static text, title bars, windows and menus alike.
    /// <c>false</c> is that property's default, not an assertion: no form rule declares the element
    /// valid, which is true of everything that is not a form field. <c>IsRequiredForForm</c> shares the
    /// default and is safe only because <c>required</c> is emitted on <c>true</c>, a positive claim.
    /// The property cannot distinguish "not applicable" from "invalid" on any measured stack, so it is
    /// not read and <c>invalid</c> stays unproduced rather than faked.
    /// </para>
    /// <para>
    /// <c>pressed</c> is produced from <c>LegacyIAccessibleState</c>. A <c>button</c> role with the
    /// <c>STATE_SYSTEM_PRESSED</c> bit set emits <c>pressed</c>. This covers the toolbar toggle button
    /// measured advertising no <c>TogglePattern</c>, only <c>Invoke</c> and <c>LegacyIAccessible</c>,
    /// and carrying its pressed state in the legacy bit alone. Buttons that advertise
    /// <c>ToggleAvailable</c> are reclassified to <c>switch</c> by role resolution, so those surface as
    /// <c>switch</c> plus <c>checked</c>; a <c>button</c> reaching this method with the bit set is a
    /// button whose pressed state is exposed only through the legacy MSAA interface.
    /// </para>
    /// <para>
    /// Known versus unknown: <c>LocatorField.Unknown</c> is returned only when every
    /// <see cref="ReadHealthProbes"/> source came back <c>PropertyOutcome.Unknown</c>, the shape a
    /// property nobody read produces. That means the state read never happened, which is not the same
    /// fact as "this element carries no states", so it must not be reported as an empty known vector. A
    /// single probe that came back known or absent proves the batch reached this element, and from then
    /// on an empty vector is a legitimate answer.
    /// </para>
    /// </remarks>
    public static LocatorField<List<string>> ResolveStates(
        ElementProperties properties,
        LocatorField<string> role)
    {
        if (ReadHealthFailed(properties))
        {
            return LocatorField<List<string>>.Unknown();
        }

        var roleName = role.Value ?? string.Empty;
        var states = new List<string>();

        if (properties.Get(TreeProperty.IsEnabled).Flag() == false)
        {
            states.Add(DisabledToken);
        }
        if (properties.Get(TreeProperty.IsPassword).Flag() == true)
        {
            states.Add(SecureToken);
        }
        if (properties.Get(TreeProperty.IsOffscreen).Flag() == true)
        {
            states.Add(OffscreenToken);
        }
        if (properties.Get(TreeProperty.HasKeyboardFocus).Flag() == true)
        {
            states.Add(FocusedToken);
        }
        if (properties.Get(TreeProperty.IsRequiredForForm).Flag() == true)
        {
            states.Add(RequiredToken);
        }

        PushToggleState(properties, roleName, states);
        PushExpandCollapseState(properties, states);

        if (properties.GatedBool(TreeProperty.SelectionItemIsSelected) == true)
        {
            states.Add(SelectedToken);
        }
        if (properties.GatedBool(TreeProperty.ValueIsReadOnly) == true)
        {
            states.Add(ReadonlyToken);
        }
        if (properties.GatedBool(TreeProperty.SelectionCanSelectMultiple) == true)
        {
            states.Add(MultiselectableToken);
        }
        if (properties.GatedBool(TreeProperty.WindowIsModal) == true)
        {
            states.Add(ModalToken);
        }

        PushLegacyState(properties, roleName, states);

        return LocatorField<List<string>>.Known(states);
    }

    private static bool ReadHealthFailed(ElementProperties properties)
    {
        return ReadHealthProbes.All(property => properties.Get(property).IsUnknown());
    }

    /// <summary>
    /// <c>ToggleState</c>, gated on the element's role. Only the roles that carry a toggle emit
    /// <c>checked</c> and <c>indeterminate</c>.
    /// </summary>
    private static void PushToggleState(ElementProperties properties, string role, List<string> states)
    {
        var toggle = properties.GatedNumber(TreeProperty.ToggleState);
        if (toggle is null || !IsToggleableRole(role))
        {
            return;
        }

        switch (toggle.Value)
        {
            case ToggleStateOn:
                states.Add(CheckedToken);
                break;
            case ToggleStateIndeterminate:
                states.Add(IndeterminateToken);
                break;
        }
    }

    /// <summary>
    /// The toggleable role set, mirrored from the Rust <c>roles::is_toggleable_role</c>.
    /// </summary>
    private static bool IsToggleableRole(string role) => role is "checkbox" or "switch" or "radiobutton";

    /// <summary>
    /// <c>ExpandCollapseState</c>. <c>LeafNode</c> is a fourth value this vocabulary does not model as a
    /// token: the pattern is implemented but the node never expands, which is neither <c>expanded</c>
    /// nor a collapsed state.
    /// </summary>
    private static void PushExpandCollapseState(ElementProperties properties, List<string> states)
    {
        if (properties.GatedNumber(TreeProperty.ExpandCollapseState) == ExpandCollapseStateExpanded)
        {
            states.Add(ExpandedToken);
        }
    }

    /// <summary>
    /// <c>LegacyIAccessibleState</c>, the Windows source for <c>pressed</c>, <c>haspopup</c> and
    /// <c>busy</c>. Neither <c>haspopup</c> nor <c>busy</c> has a UI Automation property of its own, and
    /// the measured bitmask discriminates between those two bits.
    /// </summary>
    private static void PushLegacyState(ElementProperties properties, string role, List<string> states)
    {
        var bits = properties.Get(TreeProperty.LegacyState).Number();
        if (bits is null)
        {
            return;
        }

        var value = bits.Value;
        if (role == "button" && (value & StateSystemPressed) != 0)
        {
            states.Add(PressedToken);
        }
        if ((value & StateSystemHaspopup) != 0)
        {
            states.Add(HaspopupToken);
        }
        if ((value & StateSystemBusy) != 0)
        {
            states.Add(BusyToken);
        }
    }
}
