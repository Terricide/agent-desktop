using AgentDesktop.Windows.Actions;
using Xunit;

namespace AgentDesktop.Windows.Tests.Actions;

public sealed class SelectSearchTests
{
    [Fact(Skip = "requires a live UIA tree")]
    public void FirstMatchStillRealizesBeforeSelect()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void MidRealizeSearchAmbiguityAborts()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void PostRealizeDuplicateIsAmbiguous()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void MissAfterRealizeStillCollapsesWhenExpanded()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void SelfMatchSelectsAndVerifiesIsSelected()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void ValueMismatchIsElementNotFoundWithCharCountNotText()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void ContainerSearchSelectsWhenFindHits()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void BudgetExhaustionSurfacesHonestError()
    {
    }

    [Fact(Skip = "requires a live UIA tree")]
    public void CollapsedComboboxExpandsFirstAndCollapsesOnFailure()
    {
    }

    [Fact]
    public void ContainerValueVerificationOutranksIsSelected()
    {
        Assert.Equal(true, SelectAction.ResolveSelectVerification(hasContainerValue: true, containerMatched: true, isSelected: false));
        Assert.Equal(false, SelectAction.ResolveSelectVerification(hasContainerValue: true, containerMatched: false, isSelected: true));
        Assert.Equal(true, SelectAction.ResolveSelectVerification(hasContainerValue: true, containerMatched: null, isSelected: true));
    }
}
