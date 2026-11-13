using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Actions;
using Xunit;

namespace AgentDesktop.Windows.Tests.Actions;

/// <summary>Budget-expiry cases for the activation chain, mirroring chain_budget_tests.rs.
/// They assert what an expiry reports rather than what a rung does.</summary>
public sealed class ChainBudgetTests
{
    private static readonly ChainDef Def = new("retry", true);

    [Fact]
    public void BudgetExpiryAfterAnUnverifiedDeliveryReportsDeliveredUnverified()
    {
        var error = Assert.Throws<AdapterError>(() => Chain.ExecuteChain(
            Deadline.After(300),
            Def,
            InteractionPolicy.Semantic,
            new[]
            {
                new ChainRung("ValuePattern.SetValue", false, () =>
                {
                    Thread.Sleep(400);
                    return DeliveryOutcome.DeliveredUnverified;
                }),
                new ChainRung("RangeValuePattern.SetValue", false, () => DeliveryOutcome.DeliveredVerified),
            }));

        Assert.Equal(ErrorCode.Timeout, error.Code);
        Assert.Equal(Delivery.DeliveredUnverified, error.Disposition.Delivery);
    }

    [Fact]
    public void BudgetExpiryBeforeAnyRungReportsNotDelivered()
    {
        var error = Assert.Throws<AdapterError>(() => Chain.ExecuteChain(
            Deadline.After(0),
            Def,
            InteractionPolicy.Semantic,
            new[] { new ChainRung("ValuePattern.SetValue", false, () => DeliveryOutcome.DeliveredVerified) }));

        Assert.Equal(ErrorCode.Timeout, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
    }

    [Fact]
    public void CappedVerificationEndRefusesAnExhaustedBudget()
    {
        Assert.Throws<AdapterError>(() => Chain.CappedVerificationEnd(Deadline.After(0), TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void CappedVerificationEndNeverExceedsTheRemainingBudget()
    {
        var deadline = Deadline.After(50);
        var end = Chain.CappedVerificationEnd(deadline, TimeSpan.FromMilliseconds(5_000));

        Assert.True(end <= DateTime.UtcNow.AddMilliseconds(50 + 5));
    }
}
