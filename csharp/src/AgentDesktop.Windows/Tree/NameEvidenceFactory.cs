using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>Supplies the name-evidence slots this platform can fill, and computes nothing.
/// The name itself comes from <see cref="AccName"/>, the one precedence in the product. This
/// function's whole job is to say what each slot holds and how certain it is.</summary>
internal static class NameEvidenceFactory
{
    /// <summary>What reading an element's `LabeledBy` relation produced. The four outcomes are
    /// not interchangeable, and collapsing any two of them would either lose a name or publish
    /// one that should have been withheld.</summary>
    internal static LabelOutcome LabelFromTarget(bool targetIsSecure, string? targetName)
    {
        if (targetIsSecure)
        {
            return LabelOutcome.Withheld;
        }

        if (targetName is null)
        {
            return LabelOutcome.Failed;
        }

        return string.IsNullOrWhiteSpace(targetName) ? LabelOutcome.Unlabelled : LabelOutcome.Labelled(targetName);
    }

    /// <summary>The fail-closed policy behind the target's `IsPassword` read: a property that
    /// could not be read is not evidence the target is safe, so it is treated the same as a
    /// confirmed secure target.</summary>
    internal static bool SecureFromRead(LocatorField<object> read) => read.Kind switch
    {
        LocatorFieldKind.Known => read.Value is true,
        _ => true,
    };

    internal static NameEvidence EvidenceOf(NodeProperties properties, LabelOutcome label) => new()
    {
        NativeTitle = properties.NonBlankText(PropertyIds.Name),
        LabelledByText = label.Kind == LabelOutcomeKind.Labelled ? label.Text : null,
        Description = DescriptionOf(properties),
        Placeholder = PlaceholderOf(properties),
        ExplicitLabel = null,
        StaticValue = null,
        ChildLabel = null,
    };

    /// <summary>Folds this platform's own gating into the per-slot status before core sees it,
    /// so no UIA concept crosses into shared code.</summary>
    internal static NameSlotStatus StatusOf(NodeProperties properties, LabelOutcome label) => new()
    {
        NativeTitle = SlotOf(properties.Get(PropertyIds.Name)),
        LabelledByText = LabelSlot(properties, label),
        Description = DescriptionSlot(properties),
        Placeholder = PlaceholderSlot(properties),
        ExplicitLabel = SlotStatus.Suppressed,
        StaticValue = SlotStatus.Suppressed,
        ChildLabel = SlotStatus.Suppressed,
    };

    /// <summary>`FullDescription` is the description, with `HelpText` as its fallback. The
    /// choice is made here, once, rather than per call site. `HelpText` never doubles as a
    /// placeholder: a property serving two slots is how the same string ends up reported twice
    /// under different names.</summary>
    internal static string? DescriptionOf(NodeProperties properties) =>
        properties.NonBlankText(PropertyIds.FullDescription) ?? properties.NonBlankText(PropertyIds.HelpText);

    /// <summary>The placeholder input slot, filled from `HelpText` where it is not already the
    /// description. The slot exists so a placeholder-only control (a text field whose only
    /// accessible text is its prompt) can still receive a name.</summary>
    internal static string? PlaceholderOf(NodeProperties properties) =>
        properties.NonBlankText(PropertyIds.FullDescription) is not null
            ? properties.NonBlankText(PropertyIds.HelpText)
            : null;

    /// <summary>The placeholder slot's status, mirroring the source rule: when
    /// `FullDescription` is a real answer, `HelpText` is free to be the placeholder and the slot
    /// tracks the `HelpText` read's certainty; when it is blank or failed, `HelpText` is the
    /// description, there is no placeholder claim, and the slot is suppressed so the placeholder
    /// never double-reports the description.</summary>
    internal static SlotStatus PlaceholderSlot(NodeProperties properties)
    {
        if (properties.NonBlankText(PropertyIds.FullDescription) is not null)
        {
            return SlotOf(properties.Get(PropertyIds.HelpText));
        }

        return SlotStatus.Suppressed;
    }

    /// <summary>The label slot's status, which is where the provider-authority rule lives. A
    /// resolved label is suppressed outright when `Name` already answered, rather than being
    /// allowed to outrank it. When `Name` is empty the label is admitted, which is the case it
    /// exists for.</summary>
    internal static SlotStatus LabelSlot(NodeProperties properties, LabelOutcome label)
    {
        if (properties.NonBlankText(PropertyIds.Name) is not null)
        {
            return SlotStatus.Suppressed;
        }

        return label.Kind switch
        {
            LabelOutcomeKind.Labelled => SlotStatus.Certain,
            LabelOutcomeKind.Failed => SlotStatus.Uncertain,
            _ => SlotStatus.Suppressed,
        };
    }

    /// <summary>A non-blank answer from either source outranks everything else: one property
    /// producing real text is a real answer about the element, even if the other read failed.
    /// Only once neither source produced non-blank text does a failed read matter, and then it
    /// must cloud the slot rather than let a provider that definitively has nothing look the
    /// same as a read that simply broke.</summary>
    internal static SlotStatus DescriptionSlot(NodeProperties properties)
    {
        var full = properties.Get(PropertyIds.FullDescription);
        var help = properties.Get(PropertyIds.HelpText);
        if (NonBlank(full) is not null || NonBlank(help) is not null)
        {
            return SlotStatus.Certain;
        }

        return IsUnknown(full) || IsUnknown(help) ? SlotStatus.Uncertain : SlotStatus.Certain;
    }

    internal static SlotStatus SlotOf(LocatorField<object> read) =>
        IsUnknown(read) ? SlotStatus.Uncertain : SlotStatus.Certain;

    private static bool IsUnknown(LocatorField<object> read) => read.Kind == LocatorFieldKind.Unknown;

    private static string? NonBlank(LocatorField<object> read) =>
        read.Value is string text && !string.IsNullOrWhiteSpace(text) ? text : null;
}
