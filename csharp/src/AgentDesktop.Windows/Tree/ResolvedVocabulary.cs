using AgentDesktop.Core.Locator;

namespace AgentDesktop.Windows.Tree;

/// <summary>What the vocabulary modules made of one element's read set, resolved together from the same batch.</summary>
internal sealed record ResolvedVocabulary(
    LocatorField<string> Role,
    LocatorField<List<string>> AvailableActions,
    LocatorField<List<string>> States,
    LocatorField<string> Name,
    LocatorField<string> Description)
{
    /// <summary>The answer for an element nothing was read from: every slot unknown.</summary>
    internal static ResolvedVocabulary Unknown() => new(
        LocatorField<string>.Unknown(),
        LocatorField<List<string>>.Unknown(),
        LocatorField<List<string>>.Unknown(),
        LocatorField<string>.Unknown(),
        LocatorField<string>.Unknown());
}
