using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using Xunit;

namespace AgentDesktop.Tests.Locator;

public class FindSelectionTests
{
    private static List<Match> Matches(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new Match(new AccessibilityNode { Role = "button", RefId = $"e{index}" }, new List<string>()))
            .ToList();

    [Fact]
    public void FirstTakesOneMatchFromTheTop()
    {
        var chosen = FindSelection.Pick(new FindSelectionArgs { First = true }, Matches(3));

        Assert.Single(chosen);
        Assert.Equal("e0", chosen[0].Node.RefId);
    }

    [Fact]
    public void LastTakesOneMatchFromTheBottom()
    {
        var chosen = FindSelection.Pick(new FindSelectionArgs { Last = true }, Matches(3));

        Assert.Single(chosen);
        Assert.Equal("e2", chosen[0].Node.RefId);
    }

    [Fact]
    public void NthIsZeroIndexedAndANthBeyondTheMatchesReturnsNothing()
    {
        Assert.Equal("e2", FindSelection.Pick(new FindSelectionArgs { Nth = 2 }, Matches(3))[0].Node.RefId);
        Assert.Empty(FindSelection.Pick(new FindSelectionArgs { Nth = 3 }, Matches(3)));
    }

    [Fact]
    public void TheDefaultLimitTruncatesAndZeroAsksForTheWholeList()
    {
        var many = Matches(FindSelection.DefaultLimit + 10);

        Assert.Equal(FindSelection.DefaultLimit, FindSelection.Pick(new FindSelectionArgs(), many).Count);
        Assert.Equal(FindSelection.DefaultLimit + 10, FindSelection.Pick(new FindSelectionArgs { Limit = 0 }, many).Count);
        Assert.Equal(3, FindSelection.Pick(new FindSelectionArgs { Limit = 3 }, many).Count);
    }

}
