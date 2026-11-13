using AgentDesktop.Core.Input;
using AgentDesktop.Windows.Input;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

public sealed class BlockedComboTests
{
    private static KeyCombo Combo(string key, params Modifier[] modifiers) =>
        new() { Key = key, Modifiers = modifiers.ToList() };

    [Fact]
    public void EveryWindowsDangerousComboIsBlocked()
    {
        Assert.True(BlockedCombo.IsBlocked(Combo("f4", Modifier.Alt)), "alt+f4");
        Assert.True(BlockedCombo.IsBlocked(Combo("l", Modifier.Meta)), "win+l");
        Assert.True(BlockedCombo.IsBlocked(Combo("d", Modifier.Meta)), "win+d");
        Assert.True(BlockedCombo.IsBlocked(Combo("tab", Modifier.Alt)), "alt+tab");
    }

    [Fact]
    public void ANonDangerousComboIsNeverBlocked()
    {
        Assert.False(BlockedCombo.IsBlocked(Combo("c", Modifier.Ctrl)));
        Assert.False(BlockedCombo.IsBlocked(Combo("a")));
        Assert.False(BlockedCombo.IsBlocked(Combo("s", Modifier.Ctrl, Modifier.Shift)));
    }

    [Fact]
    public void CtrlAltDeleteIsNotClaimedAsBlocked()
    {
        Assert.False(BlockedCombo.IsBlocked(Combo("delete", Modifier.Ctrl, Modifier.Alt)));
    }

    [Fact]
    public void ModifierReorderingDoesNotEvadeTheBlock()
    {
        var orderings = new[]
        {
            (Forward: Combo("esc", Modifier.Ctrl, Modifier.Shift), Reversed: Combo("esc", Modifier.Shift, Modifier.Ctrl)),
            (Forward: Combo("tab", Modifier.Alt, Modifier.Shift), Reversed: Combo("tab", Modifier.Shift, Modifier.Alt)),
        };
        foreach (var (forward, reversed) in orderings)
        {
            Assert.True(BlockedCombo.IsBlocked(forward), BlockedCombo.ComboToString(forward));
            Assert.True(BlockedCombo.IsBlocked(reversed), BlockedCombo.ComboToString(reversed));
            var (forwardModifiers, forwardKey) = BlockedCombo.CanonicalParts(BlockedCombo.ComboToString(forward));
            var (reversedModifiers, reversedKey) = BlockedCombo.CanonicalParts(BlockedCombo.ComboToString(reversed));
            Assert.Equal(forwardKey, reversedKey);
            Assert.Equal(forwardModifiers, reversedModifiers);
        }
    }

    [Fact]
    public void KeyNameAliasesDoNotEvadeTheBlock()
    {
        AssertPartsEqual("alt+f4", "ALT+F4");
        AssertPartsEqual("win+l", "meta+l");
        AssertPartsEqual("win+d", "cmd+d");
    }

    private static void AssertPartsEqual(string raw, string alias)
    {
        var (modifiers, key) = BlockedCombo.CanonicalParts(raw);
        var (aliasModifiers, aliasKey) = BlockedCombo.CanonicalParts(alias);
        Assert.Equal(key, aliasKey);
        Assert.Equal(modifiers, aliasModifiers);
    }

    [Fact]
    public void CanonicalKeyFoldsDocumentedAliases()
    {
        Assert.Equal("esc", BlockedCombo.CanonicalKey("escape"));
        Assert.Equal("esc", BlockedCombo.CanonicalKey("esc"));
        Assert.Equal("delete", BlockedCombo.CanonicalKey("backspace"));
        Assert.Equal("delete", BlockedCombo.CanonicalKey("delete"));
        Assert.Equal("return", BlockedCombo.CanonicalKey("enter"));
        Assert.Equal("return", BlockedCombo.CanonicalKey("return"));
    }

    [Fact]
    public void ComboToStringPlacesModifiersBeforeTheKey()
    {
        Assert.Equal("alt+f4", BlockedCombo.ComboToString(Combo("f4", Modifier.Alt)));
    }

    [Fact]
    public void AModifierSupersetOfABlockedComboDoesNotEvadeTheBlock()
    {
        Assert.True(BlockedCombo.IsBlocked(Combo("tab", Modifier.Alt, Modifier.Shift)), "alt+shift+tab");
        Assert.True(BlockedCombo.IsBlocked(Combo("tab", Modifier.Alt, Modifier.Ctrl)), "ctrl+alt+tab");
        Assert.True(BlockedCombo.IsBlocked(Combo("f4", Modifier.Shift, Modifier.Alt)), "shift+alt+f4");
    }

    [Fact]
    public void ASupersetOfTheModifiersAloneIsNotBlocked()
    {
        Assert.False(BlockedCombo.IsBlocked(Combo("f5", Modifier.Alt, Modifier.Shift)));
        Assert.False(BlockedCombo.IsBlocked(Combo("tab", Modifier.Ctrl)));
    }

    [Fact]
    public void EachShellSurfaceShortcutIsBlocked()
    {
        Assert.True(BlockedCombo.IsBlocked(Combo("r", Modifier.Meta)), "win+r");
        Assert.True(BlockedCombo.IsBlocked(Combo("x", Modifier.Meta)), "win+x");
        Assert.True(BlockedCombo.IsBlocked(Combo("e", Modifier.Meta)), "win+e");
        Assert.True(BlockedCombo.IsBlocked(Combo("esc", Modifier.Ctrl, Modifier.Shift)), "ctrl+shift+esc");
    }

    [Fact]
    public void AModifierSupersetOfEachShellSurfaceShortcutIsStillBlocked()
    {
        Assert.True(BlockedCombo.IsBlocked(Combo("r", Modifier.Meta, Modifier.Shift)), "win+shift+r");
        Assert.True(BlockedCombo.IsBlocked(Combo("x", Modifier.Meta, Modifier.Ctrl)), "ctrl+win+x");
        Assert.True(BlockedCombo.IsBlocked(Combo("e", Modifier.Meta, Modifier.Alt)), "alt+win+e");
        Assert.True(BlockedCombo.IsBlocked(Combo("esc", Modifier.Ctrl, Modifier.Shift, Modifier.Alt)), "alt+ctrl+shift+esc");
    }

    [Fact]
    public void ANearMissOfAShellSurfaceShortcutIsNotBlocked()
    {
        Assert.False(BlockedCombo.IsBlocked(Combo("r", Modifier.Ctrl)), "ctrl+r");
        Assert.False(BlockedCombo.IsBlocked(Combo("esc", Modifier.Ctrl)), "ctrl+esc");
        Assert.False(BlockedCombo.IsBlocked(Combo("esc", Modifier.Shift)), "shift+esc");
    }

    [Fact]
    public void BothEscapeAndEscSpellingsReachTheTaskManagerGuard()
    {
        Assert.True(BlockedCombo.IsBlocked(Combo("escape", Modifier.Ctrl, Modifier.Shift)));
        Assert.True(BlockedCombo.IsBlocked(Combo("esc", Modifier.Ctrl, Modifier.Shift)));
    }

    [Fact]
    public void TheAdapterOverrideReachesTheWindowsDangerousList()
    {
        var adapter = new WindowsAdapter();
        Assert.True(adapter.IsBlockedCombo(Combo("f4", Modifier.Alt)));
        Assert.False(adapter.IsBlockedCombo(Combo("c", Modifier.Ctrl)));
    }
}
