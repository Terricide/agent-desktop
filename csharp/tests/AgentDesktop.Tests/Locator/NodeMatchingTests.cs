using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using Xunit;

namespace AgentDesktop.Tests.Locator;

public class NodeMatchingTests
{
    private static AccessibilityNode Node(
        string role,
        string? name = null,
        string? value = null,
        string? nativeId = null,
        List<string>? states = null,
        List<AccessibilityNode>? children = null) => new()
        {
            Role = role,
            Identity = new NodeIdentity
            {
                Name = name is null ? LocatorField<string>.Unknown() : LocatorField<string>.Known(name),
                Value = value is null ? LocatorField<string>.Unknown() : LocatorField<string>.Known(value),
                NativeId = nativeId is null ? null : new ElementIdentifier(IdentifierKind.AutomationId, nativeId),
            },
            Presentation = new NodePresentation
            {
                States = states ?? new List<string>(),
            },
            Children = children ?? new List<AccessibilityNode>(),
        };

    [Fact]
    public void AnEmptyQueryMatchesEveryNode()
    {
        Assert.True(NodeMatching.Matches(new LocatorQuery(), Node("button")));
    }

    [Fact]
    public void ARoleQueryFoldsTheWebShorthandToTheCanonicalRole()
    {
        var query = new LocatorQuery { Identity = new IdentityPredicate { Role = "img" } };

        Assert.True(NodeMatching.Matches(query, Node("image")));
        Assert.False(NodeMatching.Matches(query, Node("img")));
    }

    [Fact]
    public void ANameQueryComparesCaseAndWhitespaceAway()
    {
        var query = new LocatorQuery { Identity = new IdentityPredicate { Name = SearchText.Normalize("  OK   Button ") } };

        Assert.True(NodeMatching.Matches(query, Node("button", "ok button")));
        Assert.True(NodeMatching.Matches(query, Node("button", "OK\nButton")));
        Assert.True(NodeMatching.Matches(query, Node("button", "Open the OK Button dialog")));
        Assert.False(NodeMatching.Matches(query, Node("button", "close button")));
    }

    [Fact]
    public void BidirectionalControlCharactersAreStrippedFromAComparison()
    {
        var query = new LocatorQuery { Identity = new IdentityPredicate { Name = SearchText.Normalize("ok") } };

        Assert.True(NodeMatching.Matches(query, Node("button", "o\u200ek")));
    }

    [Fact]
    public void ANameQueryNeverMatchesANodeWhoseNameWasNeverRead()
    {
        var query = new LocatorQuery { Identity = new IdentityPredicate { Name = "ok" } };
        var unread = Node("button");

        Assert.True(unread.Identity.Name.IsUnknown());
        Assert.False(NodeMatching.Matches(query, unread));
    }

    [Fact]
    public void ANativeIdMatchIsExactStringEquality()
    {
        var query = new LocatorQuery { Identity = new IdentityPredicate { NativeId = "fixture-root" } };

        Assert.True(NodeMatching.Matches(query, Node("window", nativeId: "fixture-root")));
        Assert.False(NodeMatching.Matches(query, Node("window", nativeId: "FixtureRoot")));
        Assert.False(NodeMatching.Matches(query, Node("window")));
    }

    [Fact]
    public void AStateTokenRequiresTheStateToBePresent()
    {
        var query = new LocatorQuery { States = new List<StatePredicate> { new() { Token = "focused" } } };

        Assert.True(NodeMatching.Matches(query, Node("button", states: new List<string> { "focused" })));
        Assert.False(NodeMatching.Matches(query, Node("button")));
    }

    [Fact]
    public void AStateTokenWithFalseRequiresTheStateToBeAbsent()
    {
        var query = new LocatorQuery
        {
            States = new List<StatePredicate> { new() { Token = "focused", Expected = false } },
        };

        Assert.True(NodeMatching.Matches(query, Node("button")));
        Assert.False(NodeMatching.Matches(query, Node("button", states: new List<string> { "focused" })));
    }

    [Fact]
    public void EveryStateTokenMustHoldSoAnAddingQueryNarrowsTheMatches()
    {
        var query = new LocatorQuery
        {
            States = new List<StatePredicate>
            {
                new() { Token = "focused" },
                new() { Token = "checked" },
            },
        };

        Assert.True(NodeMatching.Matches(query, Node("button", states: new List<string> { "focused", "checked" })));
        Assert.False(NodeMatching.Matches(query, Node("button", states: new List<string> { "focused" })));
    }

    [Fact]
    public void AStateFlagParsesTheValueFormAndRefusesAnythingElse()
    {
        Assert.Equal("focused", StatePredicateParser.Parse("focused").Predicate!.Token);
        Assert.Null(StatePredicateParser.Parse("focused").Predicate!.Expected);
        Assert.True(StatePredicateParser.Parse("focused=true").Predicate!.Expected);
        Assert.True(StatePredicateParser.Parse("focused=1").Predicate!.Expected);
        Assert.False(StatePredicateParser.Parse("FOCUSED=false").Predicate!.Expected);
        Assert.Null(StatePredicateParser.Parse("focused=maybe").Predicate);
        Assert.Null(StatePredicateParser.Parse("flying").Predicate);
    }

    [Fact]
    public void ATextQueryMatchesANodeWhoseDescendantCarriesTheText()
    {
        var leaf = Node("text", "Clicks and mouse");
        var parent = Node("group", children: new List<AccessibilityNode> { leaf });
        var query = new LocatorQuery { HasText = SearchText.Normalize("clicks") };

        Assert.True(NodeMatching.Matches(query, parent));
        Assert.False(NodeMatching.Matches(query, Node("group")));
    }

    [Fact]
    public void AnExactQueryComparesTheNormalizedFormsOfTheWholeField()
    {
        var query = new LocatorQuery
        {
            Identity = new IdentityPredicate { Name = SearchText.Normalize("ok") },
            Exact = true,
        };

        Assert.True(NodeMatching.Matches(query, Node("button", "OK")));
        Assert.False(NodeMatching.Matches(query, Node("button", "ok button")));
    }
}
