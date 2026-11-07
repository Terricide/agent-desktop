using AgentDesktop.Core.Tree;
using Xunit;

namespace AgentDesktop.Tests.Tree;

public class InteractiveRolesTests
{
    [Fact]
    public void InteractiveRolesQualifyForARef()
    {
        Assert.True(InteractiveRoles.QualifiesForRef("button", new HashSet<string>()));
        Assert.True(InteractiveRoles.QualifiesForRef("Button", new HashSet<string>()));
        Assert.True(InteractiveRoles.QualifiesForRef("treeitem", new HashSet<string>()));
    }

    [Fact]
    public void TheCanonicalInteractiveSetIsTheVocabularyAdaptersEmit()
    {
        Assert.False(InteractiveRoles.Names.Contains("MenuItemMarkChecked"));
        Assert.True(InteractiveRoles.Names.Contains("menuitem"));
    }

    [Fact]
    public void TheThreeAlwaysAdvertisedActionsDoNotQualifyARef()
    {
        var web = new HashSet<string> { "AXSetFocus", "AXRightClick", "AXScrollTo" };
        Assert.False(InteractiveRoles.QualifiesForRef("statictext", web));
        Assert.False(InteractiveRoles.QualifiesForRef("group", web));
    }

    [Fact]
    public void APrimaryActionBeyondTheThreeQualifiesARef()
    {
        Assert.True(InteractiveRoles.QualifiesForRef("statictext", new HashSet<string> { "AXPress" }));
    }

    [Fact]
    public void ACanonicalLowercaseRoleQualifiesWithoutAnyPrimaryAction()
    {
        var onlyTheThree = new HashSet<string> { "SetFocus", "ScrollTo", "RightClick" };
        Assert.True(InteractiveRoles.QualifiesForRef("textfield", onlyTheThree));
        Assert.True(InteractiveRoles.QualifiesForRef("checkbox", onlyTheThree));
        Assert.True(InteractiveRoles.QualifiesForRef("button", onlyTheThree));
    }
}
