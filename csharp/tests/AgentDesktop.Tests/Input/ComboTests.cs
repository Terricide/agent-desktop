using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using Xunit;

namespace AgentDesktop.Tests.Input;

/// <summary>Verifies the key-combo string the CLI accepts: modifier names, the implicit Shift an
/// uppercase letter carries, and the refusals a malformed combo earns before any key is pressed.</summary>
public sealed class ComboTests
{
    [Fact]
    public void AComboParsesModifiersThenTheKey()
    {
        var combo = Combo.Parse("ctrl+shift+tab");

        Assert.Equal("tab", combo.Key);
        Assert.Equal([Modifier.Ctrl, Modifier.Shift], combo.Modifiers);
    }

    [Fact]
    public void ModifierAliasesNameTheSameKeys()
    {
        var combo = Combo.Parse("cmd+c");

        Assert.Equal("c", combo.Key);
        Assert.Equal([Modifier.Meta], combo.Modifiers);
        Assert.Equal("c", Combo.Parse("command+c").Key);
        Assert.Equal(Modifier.Meta, Combo.Parse("super+z").Modifiers[0]);
        Assert.Equal(Modifier.Ctrl, Combo.Parse("control+c").Modifiers[0]);
        Assert.Equal(Modifier.Alt, Combo.Parse("option+x").Modifiers[0]);
    }

    [Fact]
    public void SpacesInAComboAreIgnored()
    {
        var combo = Combo.Parse("ctrl + c");

        Assert.Equal("c", combo.Key);
        Assert.Equal([Modifier.Ctrl], combo.Modifiers);
    }

    [Fact]
    public void AnUppercaseLetterCarriesTheShiftItsNameCannotExpress()
    {
        var combo = Combo.Parse("A");

        Assert.Equal("a", combo.Key);
        Assert.Equal([Modifier.Shift], combo.Modifiers);
    }

    [Fact]
    public void AnExplicitModifierSuppressesTheImplicitShift()
    {
        var combo = Combo.Parse("Ctrl+C");

        Assert.Equal("c", combo.Key);
        Assert.Equal([Modifier.Ctrl], combo.Modifiers);
    }

    [Fact]
    public void TheRawComboIsKeptSeparateSoASafetyCheckSeesWhatTheUserTyped()
    {
        var raw = Combo.ParseModifiers("A");
        var pressed = Combo.Parse("A");

        Assert.Empty(raw.Modifiers);
        Assert.Single(pressed.Modifiers);
    }

    [Fact]
    public void AnUnknownModifierIsRefusedBeforeAnyKeyIsPressed()
    {
        var error = Assert.Throws<AdapterError>(() => Combo.Parse("hyper+c"));

        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
        Assert.Contains("hyper", error.Message);
    }

    [Fact]
    public void AComboWithNoKeyIsRefused()
    {
        var error = Assert.Throws<AdapterError>(() => Combo.Parse("ctrl+"));

        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
    }

    [Fact]
    public void AComboNamedWithOnlyAKeyNeedsNoModifier()
    {
        var combo = Combo.Parse("return");

        Assert.Equal("return", combo.Key);
        Assert.Empty(combo.Modifiers);
    }
}
