using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>Supplies the name-evidence slots this platform can fill, and computes nothing.
/// The name itself comes from <see cref="AccName"/>, the one precedence in the product. This
/// function's whole job is to say what each slot holds and how certain it is.</summary>
internal static class NameEvidenceFactory
{
    /// <summary>What reading an element's <c>LabeledBy</c> relation produced. The COM reads
    /// happen here; the policy on what they mean lives in <see cref="LabelFromTarget"/>, which
    /// this function only calls. The relation is one more property in a batch already being
    /// fetched, and the target's <c>Name</c> and <c>IsPassword</c> are read live, for the
    /// labelled nodes alone.</summary>
    internal static LabelOutcome ReadLabel(Element element, bool cached)
    {
        var target = cached ? element.CachedElementOf(TreeProperty.LabeledBy) : element.CurrentElementOf(TreeProperty.LabeledBy);
        if (target is null)
        {
            return LabelOutcome.Unlabelled;
        }

        using (target)
        {
            return LabelFromTarget(IsSecure(target), ReadName(target));
        }
    }

    private static string? ReadName(Element target) =>
        target.CurrentName().IsKnown() ? target.CurrentName().Value : null;

    private static bool IsSecure(Element target) =>
        SecureFromRead(target.CurrentProperty(PropertyUia.ToUiaProperty(TreeProperty.IsPassword)));

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

    internal static NameEvidence EvidenceOf(ElementProperties properties, LabelOutcome label) => new()
    {
        NativeTitle = properties.Get(TreeProperty.Name).NonBlankText(),
        LabelledByText = label.Kind == LabelOutcomeKind.Labelled ? label.Text : null,
        Description = DescriptionOf(properties),
        Placeholder = PlaceholderOf(properties),
        ExplicitLabel = null,
        StaticValue = null,
        ChildLabel = null,
    };

    /// <summary>Folds this platform's own gating into the per-slot status before core sees it,
    /// so no UIA concept crosses into shared code.</summary>
    internal static NameSlotStatus StatusOf(ElementProperties properties, LabelOutcome label) => new()
    {
        NativeTitle = SlotOf(properties.Get(TreeProperty.Name)),
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
    internal static string? DescriptionOf(ElementProperties properties) =>
        properties.Get(TreeProperty.FullDescription).NonBlankText() ?? properties.Get(TreeProperty.HelpText).NonBlankText();

    /// <summary>The placeholder input slot, filled from `HelpText` where it is not already the
    /// description. The slot exists so a placeholder-only control (a text field whose only
    /// accessible text is its prompt) can still receive a name.</summary>
    internal static string? PlaceholderOf(ElementProperties properties) =>
        properties.Get(TreeProperty.FullDescription).NonBlankText() is not null
            ? properties.Get(TreeProperty.HelpText).NonBlankText()
            : null;

    /// <summary>The placeholder slot's status, mirroring the source rule: when
    /// `FullDescription` is a real answer, `HelpText` is free to be the placeholder and the slot
    /// tracks the `HelpText` read's certainty; when it is blank or failed, `HelpText` is the
    /// description, there is no placeholder claim, and the slot is suppressed so the placeholder
    /// never double-reports the description.</summary>
    internal static SlotStatus PlaceholderSlot(ElementProperties properties)
    {
        if (properties.Get(TreeProperty.FullDescription).NonBlankText() is not null)
        {
            return SlotOf(properties.Get(TreeProperty.HelpText));
        }

        return SlotStatus.Suppressed;
    }

    /// <summary>The label slot's status, which is where the provider-authority rule lives. A
    /// resolved label is suppressed outright when `Name` already answered, rather than being
    /// allowed to outrank it. When `Name` is empty the label is admitted, which is the case it
    /// exists for.</summary>
    internal static SlotStatus LabelSlot(ElementProperties properties, LabelOutcome label)
    {
        if (properties.Get(TreeProperty.Name).NonBlankText() is not null)
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
    internal static SlotStatus DescriptionSlot(ElementProperties properties)
    {
        var full = properties.Get(TreeProperty.FullDescription);
        var help = properties.Get(TreeProperty.HelpText);
        if (full.NonBlankText() is not null || help.NonBlankText() is not null)
        {
            return SlotStatus.Certain;
        }

        return full.IsUnknown() || help.IsUnknown() ? SlotStatus.Uncertain : SlotStatus.Certain;
    }

    internal static SlotStatus SlotOf(PropertyOutcome outcome) =>
        outcome.IsUnknown() ? SlotStatus.Uncertain : SlotStatus.Certain;
}
