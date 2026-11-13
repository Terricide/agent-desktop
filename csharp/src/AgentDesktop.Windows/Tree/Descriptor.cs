using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// Produces the descriptor group from the read set. Mirrors crates/windows/src/tree/descriptor.rs.
/// </summary>
/// <remarks>
/// Every slot is a positive claim: a failed or gated source read contributes nothing, per the tri-state rule.
/// All four sources are <c>Known</c>-only emissions resolved by the same walk batch, so nothing here costs a round
/// trip.
///
/// Sources, each confirmed by the A16 probe family:
/// - <c>RoleDescription</c> comes from <c>LocalizedControlType</c> (A16-5: populated on every control type across
///   all three stacks, zero failed reads; the content is the provider's display vocabulary, so it is not target
///   text and never withheld by the secure gate).
/// - <c>Subrole</c> comes from <c>AriaRole</c> (A16-6: non-empty on 164 of 165 settled Chromium elements with short
///   ARIA vocabulary tokens; a failed read produces no subrole). <c>AriaRole</c> is provider vocabulary by the
///   caller's pre-committed branch — the verbatim-author-text sub-question returned zero on this box, so it is not
///   target text either.
/// - <c>Placeholder</c> comes from <c>HelpText</c>, but only where <c>HelpText</c> is not already the description
///   (the richer <c>FullDescription</c> wins the description slot).
/// - <c>DomClasses</c> has no source on the pinned stack (A16-6 measured <c>AriaProperties</c> carrying no
///   <c>class</c> token, and <c>HtmlClass</c> does not exist in <c>uiautomation</c> 0.25.0), so it stays empty. The
///   schema is settled for whichever platform first produces it, and this one does not yet.
/// </remarks>
internal static class Descriptor
{
    /// <summary>
    /// Builds the descriptor group from the read set.
    /// </summary>
    internal static NodeDescriptor BuildDescriptors(ElementProperties properties)
    {
        return new NodeDescriptor
        {
            Subrole = NonEmptyText(properties.Get(TreeProperty.AriaRole)),
            RoleDescription = NonEmptyText(properties.Get(TreeProperty.LocalizedControlType)),
            Placeholder = ResolvePlaceholder(properties),
            DomClasses = new List<string>(),
        };
    }

    /// <summary>
    /// The placeholder: <c>HelpText</c> where it is not already the description.
    /// </summary>
    /// <remarks>
    /// The description slot is <c>FullDescription</c> with <c>HelpText</c> as its fallback
    /// (<c>name_evidence.rs::description_of</c>). So <c>HelpText</c> serves as the description exactly when
    /// <c>FullDescription</c> is blank or failed; only when <c>FullDescription</c> is a real, non-blank answer can
    /// <c>HelpText</c> double as the placeholder instead. A property serving two slots is how the same string ends
    /// up reported twice under different names.
    /// </remarks>
    internal static string? ResolvePlaceholder(ElementProperties properties)
    {
        var full = NonEmptyText(properties.Get(TreeProperty.FullDescription));
        return full is not null ? NonEmptyText(properties.Get(TreeProperty.HelpText)) : null;
    }

    private static string? NonEmptyText(PropertyOutcome outcome) => outcome.NonBlankText();
}
