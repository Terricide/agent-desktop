using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// Pins the Windows side of the shared name precedence: what each slot holds, how certain it
/// is, and the two rules that are platform policy rather than shared precedence — provider
/// authority over `Name`, and the cross-element secure gate on a label.
public sealed class NameEvidenceFactoryTests
{
    private static NodeProperties Props(
        string? name = null,
        string? helpText = null,
        string? fullDescription = null,
        LocatorField<object>? fullDescriptionRead = null,
        LocatorField<object>? helpTextRead = null,
        LocatorField<object>? nameRead = null)
    {
        var reads = new Dictionary<int, LocatorField<object>>
        {
            [PropertyIds.IsPassword] = LocatorField<object>.Known(false),
            [PropertyIds.Name] = NodeProperties.AbsentRead,
            [PropertyIds.HelpText] = NodeProperties.AbsentRead,
            [PropertyIds.FullDescription] = NodeProperties.AbsentRead,
        };
        if (name is not null) reads[PropertyIds.Name] = LocatorField<object>.Known(name);
        if (helpText is not null) reads[PropertyIds.HelpText] = LocatorField<object>.Known(helpText);
        if (fullDescription is not null) reads[PropertyIds.FullDescription] = LocatorField<object>.Known(fullDescription);
        if (nameRead is not null) reads[PropertyIds.Name] = nameRead;
        if (fullDescriptionRead is not null) reads[PropertyIds.FullDescription] = fullDescriptionRead;
        if (helpTextRead is not null) reads[PropertyIds.HelpText] = helpTextRead;

        return new NodeProperties(reads);
    }

    /// The cross-element secure gate. A label's text crosses an element boundary: the referring
    /// element's own gate never sees it, because the content came from a different element's
    /// `Name`. This is the only thing standing between a secure target's text and a different,
    /// non-secure element's published name.
    [Fact]
    public void ASecureTargetNeverSuppliesALabel()
    {
        Assert.Equal(LabelOutcome.Withheld, NameEvidenceFactory.LabelFromTarget(true, "Password"));
        Assert.True(NameEvidenceFactory.LabelFromTarget(true, "Password") == LabelOutcome.Withheld);
        Assert.Equal("Password", NameEvidenceFactory.LabelFromTarget(false, "Password").Text);
    }

    /// A property that could not be read is not evidence the target is safe, so it is treated
    /// the same as a confirmed secure target.
    [Fact]
    public void AnUnreadIsPasswordFailsClosedAsSecure()
    {
        Assert.True(NameEvidenceFactory.SecureFromRead(NodeProperties.UnknownRead));
        Assert.True(NameEvidenceFactory.SecureFromRead(NodeProperties.AbsentRead));
        Assert.True(NameEvidenceFactory.SecureFromRead(LocatorField<object>.Known(true)));
        Assert.False(NameEvidenceFactory.SecureFromRead(LocatorField<object>.Known(false)));
    }

    /// A resolved label is suppressed outright when `Name` already answered, rather than being
    /// allowed to outrank it. UIA's `Name` is the provider's own computed accessible name, which
    /// on a well-behaved provider has typically absorbed the label relationship; letting a
    /// fragment override it would publish a name the provider did not finalise.
    [Fact]
    public void ALabelCannotOverrideANameTheProviderFinalised()
    {
        var labelled = LabelOutcome.Labelled("Search box");

        Assert.Equal(SlotStatus.Suppressed,
            NameEvidenceFactory.LabelSlot(Props(name: "Search"), labelled));
        Assert.Equal(SlotStatus.Certain,
            NameEvidenceFactory.LabelSlot(Props(), labelled));
    }

    /// A failed label read clouds the slot; an absent relation does not, because the provider
    /// definitively has nothing to say.
    [Fact]
    public void AFailedLabelReadCloudsTheSlotAndAnAbsentRelationDoesNot()
    {
        Assert.Equal(SlotStatus.Uncertain, NameEvidenceFactory.LabelSlot(Props(), LabelOutcome.Failed));
        Assert.Equal(SlotStatus.Suppressed, NameEvidenceFactory.LabelSlot(Props(), LabelOutcome.Unlabelled));
        Assert.Equal(SlotStatus.Suppressed, NameEvidenceFactory.LabelSlot(Props(), LabelOutcome.Withheld));
    }

    /// When `FullDescription` is a real answer, `HelpText` is free to be the placeholder and the
    /// slot tracks the `HelpText` read's certainty. When it is blank or failed, `HelpText` is the
    /// description, there is no placeholder claim, and the slot is suppressed so the placeholder
    /// never double-reports the description.
    [Fact]
    public void APlaceholderIsSuppressedWhenHelpTextIsAlreadyTheDescription()
    {
        Assert.Equal(SlotStatus.Certain,
            NameEvidenceFactory.PlaceholderSlot(Props(fullDescription: "Details", helpText: "Type here")));
        Assert.Equal(SlotStatus.Uncertain,
            NameEvidenceFactory.PlaceholderSlot(Props(
                fullDescription: "Details",
                helpTextRead: NodeProperties.UnknownRead)));
        Assert.Equal(SlotStatus.Suppressed,
            NameEvidenceFactory.PlaceholderSlot(Props(helpText: "Type here")));
    }

    /// A non-blank answer from either source outranks everything else: one property producing
    /// real text is a real answer about the element, even if the other read failed. Only once
    /// neither source produced non-blank text does a failed read matter.
    [Fact]
    public void AFailedDescriptionReadOnlyMattersWhenNeitherSourceAnswered()
    {
        Assert.Equal(SlotStatus.Certain,
            NameEvidenceFactory.DescriptionSlot(Props(
                fullDescription: "Filters all messages",
                helpTextRead: NodeProperties.UnknownRead)));
        Assert.Equal(SlotStatus.Uncertain,
            NameEvidenceFactory.DescriptionSlot(Props(helpTextRead: NodeProperties.UnknownRead)));
        Assert.Equal(SlotStatus.Certain,
            NameEvidenceFactory.DescriptionSlot(Props()));
    }

    /// The whole path, end to end: the adapter's slots feed the shared precedence, and a label
    /// only supplies a name when `Name` has nothing to say.
    [Fact]
    public void TheAdapterFeedsTheSharedPrecedence()
    {
        var labelled = LabelOutcome.Labelled("Search box");

        Assert.Equal("Search",
            AccName.ResolveName(
                NameEvidenceFactory.EvidenceOf(Props(name: "Search"), labelled),
                NameEvidenceFactory.StatusOf(Props(name: "Search"), labelled)).Value);
        Assert.Equal("Search box",
            AccName.ResolveName(
                NameEvidenceFactory.EvidenceOf(Props(), labelled),
                NameEvidenceFactory.StatusOf(Props(), labelled)).Value);
        Assert.Null(
            AccName.ResolveDescription(
                NameEvidenceFactory.EvidenceOf(Props(), labelled),
                NameEvidenceFactory.StatusOf(Props(), labelled)).Value);
    }

    /// A placeholder-only control must still receive a name, so it can be referenced. Without
    /// the slot, such a control would be unreferenced even though UIA publishes the prompt.
    [Fact]
    public void APlaceholderOnlyControlStillGetsAName()
    {
        var evidence = NameEvidenceFactory.EvidenceOf(Props(helpText: "Type here"), LabelOutcome.Unlabelled);
        var status = NameEvidenceFactory.StatusOf(Props(helpText: "Type here"), LabelOutcome.Unlabelled);

        Assert.Equal("Type here", AccName.ResolveName(evidence, status).Value);
    }
}
