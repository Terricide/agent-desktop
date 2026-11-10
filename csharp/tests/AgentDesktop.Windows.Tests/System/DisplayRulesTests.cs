using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows;
using Xunit;

namespace AgentDesktop.Windows.Tests;

public sealed class DisplayRulesTests
{
    private static Rect R(double x, double y, double w, double h) => new(x, y, w, h);

    private static DisplayInfo D(string id, bool primary, double scale, Rect bounds) => new(id, bounds, primary, scale);

    [Fact]
    public void PrimaryDisplayOrdersFirst()
    {
        var ordered = DisplayRules.PrimariesFirst(new[]
        {
            D("monitor-2", false, 1.0, R(0, 0, 100, 100)),
            D("monitor-1", true, 1.0, R(0, 0, 100, 100)),
        });

        Assert.True(ordered[0].IsPrimary);
        Assert.Equal("monitor-1", ordered[0].Id);
    }

    [Fact]
    public void ASuccessfulReadWithAPositiveDpiYieldsAScale() =>
        Assert.Equal(1.5, DisplayRules.EffectiveDpiScale(0, 144));

    [Fact]
    public void AFailedReadIsNullEvenWithALeftoverDpi() =>
        Assert.Null(DisplayRules.EffectiveDpiScale(unchecked((int)0x8007_0057), 96));

    [Fact]
    public void ASuccessfulReadWithAZeroDpiIsNull() =>
        Assert.Null(DisplayRules.EffectiveDpiScale(0, 0));

    [Fact]
    public void AFailedInfoReadIsReportedAsItselfFirst()
    {
        Assert.Equal(EnumerationOutcome.MonitorInfoUnreadable, DisplayRules.ClassifyEnumeration(false, true, true));
        Assert.Equal(EnumerationOutcome.DpiUnreadable, DisplayRules.ClassifyEnumeration(true, false, true));
        Assert.Equal(EnumerationOutcome.EnumerationFailed, DisplayRules.ClassifyEnumeration(false, false, false));
        Assert.Equal(EnumerationOutcome.Completed, DisplayRules.ClassifyEnumeration(true, false, false));
    }

    [Fact]
    public void SelectionPrefersTheLargestOverlapThenPrimaryThenFirst()
    {
        var left = D("monitor-1", true, 1.0, R(0, 0, 100, 100));
        var right = D("monitor-2", false, 2.0, R(100, 0, 100, 100));
        var displays = new[] { left, right };

        Assert.Equal(right, DisplayRules.SelectDisplay(displays, R(150, 10, 50, 50)));
        Assert.Equal(left, DisplayRules.SelectDisplay(displays, R(500, 500, 10, 10)));
        Assert.Equal(left, DisplayRules.SelectDisplay(displays, null));
    }

    [Fact]
    public void ATieKeepsTheLastMaximalDisplayAsRustMaxByDoes()
    {
        var first = D("monitor-1", false, 1.0, R(0, 0, 100, 100));
        var second = D("monitor-2", false, 1.0, R(0, 0, 100, 100));
        Assert.Equal(second, DisplayRules.SelectDisplay(new[] { first, second }, R(0, 0, 50, 50)));
    }

    [Fact]
    public void IdentityRequiresIdBoundsPrimaryAndScale()
    {
        var expected = D("monitor-1", true, 1.5, R(0, 0, 1920, 1080));
        Assert.True(DisplayRules.DisplayIdentityMatches(expected, expected));
        Assert.False(DisplayRules.DisplayIdentityMatches(expected, expected with { Scale = 1.0 }));
        Assert.False(DisplayRules.DisplayIdentityMatches(expected, expected with { Bounds = R(0, 0, 800, 600) }));
    }
}
