using AgentDesktop.Core.Locator;

namespace AgentDesktop.Core.Tree;

/// <summary>The accessible-name precedence, and the only one in the product. Both adapters
/// call this. That is the point of it: the precedence was documented in one place and
/// implemented a second time inside an adapter, and the two disagreed. A second divergent copy
/// is the outcome this function exists to prevent.</summary>
public static class AccName
{
    /// <summary>Uncertainty travels downward, which a plain nullable-returning version
    /// structurally cannot express: if a stronger source failed to read, the answer is Unknown
    /// even when a weaker source has a value, because the value that would have won may be the
    /// one that is missing. Falling through to the weaker source would publish a name the
    /// element may not have.</summary>
    public static LocatorField<string> ResolveName(NameEvidence evidence, NameSlotStatus status)
    {
        var uncertain = false;
        foreach (var (candidate, slot) in OrderedSources(evidence, status))
        {
            if (slot == SlotStatus.Suppressed)
            {
                continue;
            }

            if (slot == SlotStatus.Uncertain)
            {
                uncertain = true;
            }

            if (NonBlank(candidate) is { } value)
            {
                return uncertain ? LocatorField<string>.Unknown() : LocatorField<string>.Known(value);
            }
        }

        return uncertain ? LocatorField<string>.Unknown() : LocatorField<string>.Absent();
    }

    /// <summary>The description, which is only a description when something else is already the
    /// name. An element whose only evidence is a description has that description as its name,
    /// so repeating it here would emit the same string twice under two labels and tell a calling
    /// agent nothing.</summary>
    public static LocatorField<string> ResolveDescription(NameEvidence evidence, NameSlotStatus status)
    {
        var stronger = StrongerSources(evidence, status);
        var named = false;
        var nameUncertain = false;
        foreach (var (candidate, slot) in stronger)
        {
            if (slot != SlotStatus.Suppressed && NonBlank(candidate) is not null)
            {
                named = true;
            }

            if (slot == SlotStatus.Uncertain)
            {
                nameUncertain = true;
            }
        }

        var suppressed = status.Description == SlotStatus.Suppressed;
        var description = suppressed ? null : NonBlank(evidence.Description);
        var uncertain = !suppressed && status.Description == SlotStatus.Uncertain;

        if (description is not null && named)
        {
            return uncertain ? LocatorField<string>.Unknown() : LocatorField<string>.Known(description);
        }

        if (description is not null && !named && nameUncertain)
        {
            return LocatorField<string>.Unknown();
        }

        if (description is null && named && uncertain)
        {
            return LocatorField<string>.Unknown();
        }

        if (description is null && !named && nameUncertain && uncertain)
        {
            return LocatorField<string>.Unknown();
        }

        return LocatorField<string>.Absent();
    }

    /// <summary>The name a caller with no uncertainty channel gets. It delegates rather than
    /// repeating the order, so the two cannot drift apart again: there is one array of sources
    /// here and both entry points read it.</summary>
    public static string? ComputeName(NameEvidence evidence) =>
        ResolveName(evidence, new NameSlotStatus()).Value;

    public static string? ComputeDescription(NameEvidence evidence) =>
        ResolveDescription(evidence, new NameSlotStatus()).Value;

    /// <summary>The precedence itself: strongest first.</summary>
    private static (string?, SlotStatus)[] OrderedSources(NameEvidence evidence, NameSlotStatus status)
    {
        var stronger = StrongerSources(evidence, status);
        return
        [
            stronger[0],
            stronger[1],
            stronger[2],
            stronger[3],
            (evidence.Description, status.Description),
            (evidence.ChildLabel, status.ChildLabel),
            (evidence.Placeholder, status.Placeholder),
        ];
    }

    /// <summary>The sources that outrank a description, which is what decides whether a
    /// description is a description at all.</summary>
    private static (string?, SlotStatus)[] StrongerSources(NameEvidence evidence, NameSlotStatus status) =>
        [
            (evidence.ExplicitLabel, status.ExplicitLabel),
            (evidence.LabelledByText, status.LabelledByText),
            (evidence.NativeTitle, status.NativeTitle),
            (evidence.StaticValue, status.StaticValue),
        ];

    /// <summary>An empty string is not text a caller reads, so it is treated the same as absent.</summary>
    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
