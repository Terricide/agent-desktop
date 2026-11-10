using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Uia;

/// <summary>What binding one pattern interface needs: the pattern id the element is queried
/// with, the mechanism name the classifier reports, and the factory that binds a pattern
/// pointer the element returned. The table is keyed by the interface type the call site asks
/// for, so a pattern is chosen by type handle, not by reflection metadata.</summary>
internal sealed record PatternContract(int Id, string Mechanism, CreatePattern Create)
{
    internal static PatternContract For(Type pattern)
    {
        if (pattern == typeof(ITogglePattern))
        {
            return new PatternContract(Slots.PatternToggle, TogglePattern.Label, TogglePattern.Create);
        }

        if (pattern == typeof(IExpandCollapsePattern))
        {
            return new PatternContract(Slots.PatternExpandCollapse, ExpandCollapsePattern.Label, ExpandCollapsePattern.Create);
        }

        if (pattern == typeof(ISelectionItemPattern))
        {
            return new PatternContract(Slots.PatternSelectionItem, SelectionItemPattern.Label, SelectionItemPattern.Create);
        }

        if (pattern == typeof(Actions.Scroll.IScrollPattern))
        {
            return new PatternContract(Slots.PatternScroll, ScrollPattern.Label, ScrollPattern.Create);
        }

        throw new AdapterError(
            ErrorCode.ActionNotSupported,
            $"{pattern.Name} is no UI Automation pattern this adapter binds")
            .WithSuggestion("Ask for Toggle, ExpandCollapse, SelectionItem, or Scroll.");
    }
}
