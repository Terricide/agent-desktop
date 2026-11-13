using AgentDesktop.Core.Context;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Actions;
using Xunit;

namespace AgentDesktop.Windows.Tests.Actions;

/// <summary>Fallthrough policy for the activation chain, mirroring chain_policy_tests.rs. The
/// shipped value-write chain declares no continuation after an unverified delivery, so a future
/// flip is caught here.</summary>
public sealed class ChainPolicyTests
{
    [Fact]
    public void ValueWriteChainStopsAfterUnverifiedDeliveryWithNoSecondRung()
    {
        var first = 0;
        var second = 0;
        var steps = Chain.ExecuteChain(
            Deadline.After(5_000),
            ValueWrite.ValueWriteChain,
            InteractionPolicy.Semantic,
            new[]
            {
                new ChainRung("ValuePattern.SetValue", false, () =>
                {
                    first++;
                    return DeliveryOutcome.DeliveredUnverified;
                }),
                new ChainRung("RangeValuePattern.SetValue", false, () =>
                {
                    second++;
                    return DeliveryOutcome.DeliveredVerified;
                }),
            });

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(steps);
        Assert.Equal("ValuePattern.SetValue", steps[0].Label);
        Assert.Equal(false, steps[0].Verified);
    }

    [Fact]
    public void ContinuingChainAdvancesPastUnverifiedDelivery()
    {
        var steps = Chain.ExecuteChain(
            Deadline.After(5_000),
            new ChainDef("retry", true),
            InteractionPolicy.Semantic,
            new[]
            {
                new ChainRung("first", false, () => DeliveryOutcome.DeliveredUnverified),
                new ChainRung("second", false, () => DeliveryOutcome.DeliveredVerified),
            });

        Assert.Equal(2, steps.Count);
        Assert.Equal("second", steps[1].Label);
    }

    [Fact]
    public void PhysicalOnlyRungIsSkippedUnderSemanticPolicyWithNoStep()
    {
        var ran = false;
        var steps = Chain.ExecuteChain(
            Deadline.After(5_000),
            new ChainDef("retry", false),
            InteractionPolicy.Semantic,
            new[]
            {
                new ChainRung("physical", true, () =>
                {
                    ran = true;
                    return DeliveryOutcome.DeliveredVerified;
                }),
                new ChainRung("semantic", false, () => DeliveryOutcome.DeliveredVerified),
            });

        Assert.False(ran);
        Assert.Single(steps);
        Assert.Equal("semantic", steps[0].Label);
    }

    [Fact]
    public void ExhaustedChainCarriesItsSuggestionAndNotDelivered()
    {
        var error = Assert.Throws<AdapterError>(() => Chain.ExecuteChain(
            Deadline.After(5_000),
            new ChainDef("target an element that advertises Invoke", false),
            InteractionPolicy.Semantic,
            new[] { new ChainRung("Invoke", false, () => DeliveryOutcome.NotDelivered) }));

        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
        Assert.Equal("target an element that advertises Invoke", error.Suggestion);
    }

    [Fact]
    public void UnverifiedDeliveryFollowedByRungErrorIsUpgradedNotRewritten()
    {
        var error = Assert.Throws<AdapterError>(() => Chain.ExecuteChain(
            Deadline.After(5_000),
            new ChainDef("retry", true),
            InteractionPolicy.Semantic,
            new[]
            {
                new ChainRung("first", false, () => DeliveryOutcome.DeliveredUnverified),
                new ChainRung("second", false, () =>
                    throw new AdapterError(ErrorCode.StaleRef, "gone")),
            }));

        Assert.Equal(ErrorCode.StaleRef, error.Code);
        Assert.Equal("gone", error.Message);
        Assert.Equal(Delivery.DeliveredUnverified, error.Disposition.Delivery);
    }
}
