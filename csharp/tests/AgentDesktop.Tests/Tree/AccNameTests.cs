using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using Xunit;

namespace AgentDesktop.Core.Tests.Tree;

public class AccNameTests
{
    private static NameEvidence Evidence() => new()
    {
        ExplicitLabel = "explicit",
        LabelledByText = "labelled",
        NativeTitle = "title",
        StaticValue = "value",
        ChildLabel = "child",
        Placeholder = "placeholder",
        Description = "description",
    };

    private static NameSlotStatus NativeTitleSlot(SlotStatus status) =>
        new() { NativeTitle = status };

    /// The order asserted here is the one that has produced every snapshot this product has
    /// ever emitted. It was not the order the shared module documented: description ranked
    /// last in the shipped precedence and fifth in the adapter that actually ran, and the
    /// documented version had no caller to notice. The shipped order won, and this test is
    /// what pins the two together.
    [Fact]
    public void NamePrecedenceIsTheShippedOrderAndIsComplete()
    {
        var value = Evidence();
        Assert.Equal("explicit", AccName.ComputeName(value));
        value = value with { ExplicitLabel = null };
        Assert.Equal("labelled", AccName.ComputeName(value));
        value = value with { LabelledByText = null };
        Assert.Equal("title", AccName.ComputeName(value));
        value = value with { NativeTitle = null };
        Assert.Equal("value", AccName.ComputeName(value));
        value = value with { StaticValue = null };
        Assert.Equal("description", AccName.ComputeName(value));
        value = value with { Description = null };
        Assert.Equal("child", AccName.ComputeName(value));
        value = value with { ChildLabel = null };
        Assert.Equal("placeholder", AccName.ComputeName(value));
    }

    /// The behaviour an Option-returning precedence structurally cannot express. A weaker
    /// source has a value, but the source that would have outranked it failed to read, so the
    /// honest answer is that the name is not known, not that it is the weaker one.
    [Fact]
    public void AFailedStrongerSourceCloudsAWeakerOneThatDidRead()
    {
        var value = new NameEvidence { ChildLabel = "child" };

        Assert.Equal(LocatorField<string>.Known("child"),
            AccName.ResolveName(value, new NameSlotStatus()));
        Assert.Equal(LocatorField<string>.Unknown(),
            AccName.ResolveName(value, NativeTitleSlot(SlotStatus.Uncertain)));
    }

    /// A suppressed slot is not a failed one: the platform is saying the slot does not apply
    /// to this element, which contributes neither a value nor doubt. Conflating the two would
    /// make every role that does not use a slot lose its name whenever that slot's read
    /// happened to fail.
    [Fact]
    public void ASuppressedSlotContributesNeitherAValueNorUncertainty()
    {
        var value = new NameEvidence { NativeTitle = "Save", ChildLabel = "child" };

        Assert.Equal(LocatorField<string>.Known("child"),
            AccName.ResolveName(value, NativeTitleSlot(SlotStatus.Suppressed)));
    }

    [Fact]
    public void NothingReadableAndNothingFailedIsAbsentRatherThanUnknown()
    {
        Assert.Equal(LocatorField<string>.Absent(),
            AccName.ResolveName(new NameEvidence(), new NameSlotStatus()));
        Assert.Equal(LocatorField<string>.Unknown(),
            AccName.ResolveName(new NameEvidence(), NativeTitleSlot(SlotStatus.Uncertain)));
    }

    /// A failed description read is Absent rather than Unknown when nothing outranks the
    /// description, and that is deliberate. With no stronger source the description becomes the
    /// name, so the description field is Absent whether the read succeeded or failed: either
    /// way there is no separate description to report. Unknown here would claim a description
    /// might exist that provably could not. The name is still Unknown in that case, which is
    /// where the uncertainty belongs. The two answers are asserted together because it is their
    /// combination that is correct, and reading either alone invites "fixing" it.
    [Fact]
    public void AFailedDescriptionIsAbsentWhenItWouldHaveBeenTheName()
    {
        var status = new NameSlotStatus { Description = SlotStatus.Uncertain };

        foreach (var evidence in new[]
        {
            new NameEvidence(),
            new NameEvidence { Description = "would have been the name" },
        })
        {
            Assert.Equal(LocatorField<string>.Absent(), AccName.ResolveDescription(evidence, status));
            Assert.Equal(LocatorField<string>.Unknown(), AccName.ResolveName(evidence, status));
        }
    }

    /// The same failed read is Unknown once something else is the name, because then a
    /// separate description could genuinely have existed.
    [Fact]
    public void AFailedDescriptionIsUnknownOnceAStrongerSourceIsTheName()
    {
        var evidence = new NameEvidence { NativeTitle = "Save" };
        var status = new NameSlotStatus { Description = SlotStatus.Uncertain };

        Assert.Equal(LocatorField<string>.Known("Save"), AccName.ResolveName(evidence, status));
        Assert.Equal(LocatorField<string>.Unknown(), AccName.ResolveDescription(evidence, status));
    }

    [Fact]
    public void BlankEvidenceIsIgnoredWithoutRewritingContent()
    {
        var value = new NameEvidence { ExplicitLabel = " \t ", NativeTitle = "  Save  " };

        Assert.Equal("  Save  ", AccName.ComputeName(value));
    }

    [Fact]
    public void DescriptionIsNotDuplicatedWhenItSuppliesTheName()
    {
        var value = new NameEvidence { Description = "Only description" };

        Assert.Equal("Only description", AccName.ComputeName(value));
        Assert.Null(AccName.ComputeDescription(value));
    }

    [Fact]
    public void DescriptionRemainsSeparateWhenAStrongerNameExists()
    {
        var value = new NameEvidence { NativeTitle = "Search", Description = "Filters all messages" };

        Assert.Equal("Search", AccName.ComputeName(value));
        Assert.Equal("Filters all messages", AccName.ComputeDescription(value));
    }

    /// Placeholder is weaker than description in the shipped order, so a placeholder does not
    /// turn a description into a description.
    [Fact]
    public void APlaceholderDoesNotOutrankADescription()
    {
        var value = new NameEvidence { Placeholder = "Search", Description = "Filters all messages" };

        Assert.Equal("Filters all messages", AccName.ComputeName(value));
        Assert.Null(AccName.ComputeDescription(value));
    }
}
