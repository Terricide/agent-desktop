using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Actions;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;
using Xunit;
using Action = AgentDesktop.Core.Actions.Action;

namespace AgentDesktop.Windows.Tests.Actions;

/// <summary>Verifies the Windows UIA activation chain, value/range writes, toggle/check/uncheck,
/// expand/collapse, scroll judgment, and HRESULT write classification without a live desktop.</summary>
public sealed class ActionChainTests
{
    [Fact]
    public void ClickChainPrefersInvokeAndFallsThroughToLegacyWhenInvokeSkipped()
    {
        var invokeSteps = ActionsAdapter.ClickChainJudgedFor(
            Deadline.Standard(),
            InteractionPolicy.Semantic,
            invokeAvailable: true,
            legacyAvailable: true,
            () => DeliveryOutcome.DeliveredUnverified,
            () => DeliveryOutcome.DeliveredUnverified);

        Assert.Single(invokeSteps);
        Assert.Equal("InvokePattern.Invoke", invokeSteps[0].Label);
        Assert.Equal("succeeded", invokeSteps[0].Outcome);
        Assert.Equal(false, invokeSteps[0].Verified);

        var fallbackSteps = ActionsAdapter.ClickChainJudgedFor(
            Deadline.Standard(),
            InteractionPolicy.Semantic,
            invokeAvailable: false,
            legacyAvailable: true,
            () => DeliveryOutcome.DeliveredUnverified,
            () => DeliveryOutcome.DeliveredUnverified);

        Assert.Equal(2, fallbackSteps.Count);
        Assert.Equal("skipped", fallbackSteps[0].Outcome);
        Assert.Equal("LegacyIAccessible.DoDefaultAction", fallbackSteps[1].Label);
        Assert.Equal("succeeded", fallbackSteps[1].Outcome);
    }

    [Fact]
    public void SetValueFallsThroughToRangeValueForFiniteNumbersAndAttachesValueCharsOnExhaustion()
    {
        var rangeSteps = ValueWrite.SetValueJudgedFor(
            Deadline.Standard(),
            InteractionPolicy.Semantic,
            "42.5",
            valueWritable: false,
            rangeAvailable: true,
            () => DeliveryOutcome.NotDelivered,
            () => DeliveryOutcome.DeliveredVerified);

        Assert.Equal(2, rangeSteps.Count);
        Assert.Equal("skipped", rangeSteps[0].Outcome);
        Assert.Equal("RangeValuePattern.SetValue", rangeSteps[1].Label);
        Assert.Equal(true, rangeSteps[1].Verified);

        var error = Assert.Throws<AdapterError>(() =>
            ValueWrite.SetValueJudgedFor(
                Deadline.Standard(),
                InteractionPolicy.Semantic,
                "secret",
                valueWritable: false,
                rangeAvailable: true,
                () => DeliveryOutcome.NotDelivered,
                () => DeliveryOutcome.DeliveredVerified));

        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
        Assert.Contains("\"value_chars\":6", error.Details ?? "");
        Assert.DoesNotContain("secret", error.Details ?? "");
    }

    [Fact]
    public void CheckAndUncheckReturnAlreadyInStateOrAdvanceTriStateToggleTwice()
    {
        var satisfied = ToggleAndDisclosure.CheckUncheckJudgedFor(
            Deadline.Standard(),
            wantChecked: true,
            toggleOk: true,
            invokeOk: false,
            () => 1,
            () => true,
            () => false);

        var satisfiedResult = ActionResult.FromExecution(new Action { Kind = ActionKind.Check }, satisfied);
        Assert.Equal(Delivery.NotDelivered, satisfiedResult.Disposition.Delivery);
        Assert.Equal(Retry.Safe, satisfiedResult.Disposition.Retry);
        Assert.Equal("AlreadyInState", satisfied[0].Label);

        var state = 2;
        var triStateSteps = ToggleAndDisclosure.CheckUncheckJudgedFor(
            Deadline.Standard(),
            wantChecked: true,
            toggleOk: true,
            invokeOk: false,
            () => state,
            () =>
            {
                state = state == 2 ? 0 : 1;
                return true;
            },
            () => false);

        Assert.Equal(2, triStateSteps.Count);
        Assert.Equal(false, triStateSteps[0].Verified);
        Assert.Equal(true, triStateSteps[1].Verified);
    }

    [Fact]
    public void DisclosureSkipsLeafNodesAndSatisfiesAlreadyExpanded()
    {
        var already = ToggleAndDisclosure.DisclosureJudgedFor(
            Deadline.Standard(),
            InteractionPolicy.Semantic,
            wantExpanded: true,
            current: 1,
            patternOk: true,
            invokeOk: true,
            () => DeliveryOutcome.DeliveredVerified,
            () => DeliveryOutcome.DeliveredVerified);

        Assert.Single(already);
        Assert.Equal("AlreadyInState", already[0].Label);
        Assert.Equal(true, already[0].Verified);

        var leafError = Assert.Throws<AdapterError>(() =>
            ToggleAndDisclosure.DisclosureJudgedFor(
                Deadline.Standard(),
                InteractionPolicy.Semantic,
                wantExpanded: true,
                current: 3,
                patternOk: true,
                invokeOk: true,
                () => DeliveryOutcome.DeliveredVerified,
                () => DeliveryOutcome.DeliveredVerified));

        Assert.Equal(ErrorCode.ActionFailed, leafError.Code);
        Assert.Equal(Delivery.NotDelivered, leafError.Disposition.Delivery);
    }

    [Fact]
    public void ScrollJudgedForReportsPartialScrollAsDeliveredUnverifiedWhenMidStepFails()
    {
        var count = 0;
        var error = Assert.Throws<AdapterError>(() =>
            ScrollAction.ScrollJudgedFor(
                Deadline.Standard(),
                scrollAvailable: true,
                axisScrollable: true,
                axisName: "vertical",
                amount: 3,
                () =>
                {
                    count++;
                    if (count == 2)
                    {
                        throw new AdapterError(ErrorCode.ActionFailed, "viewport stuck")
                            .WithDisposition(DeliverySemantics.NotDelivered());
                    }
                },
                () => true));

        Assert.Equal(Delivery.DeliveredUnverified, error.Disposition.Delivery);
        Assert.Contains("\"completed_steps\":1", error.Details ?? "");
        Assert.Contains("\"requested_steps\":3", error.Details ?? "");
    }

    [Fact]
    public void CandidateIdentityMatchesCaptureIdentityAcrossAllFields()
    {
        var props = ElementProperties.FromReads(
        [
            (TreeProperty.ControlType, new PropertyOutcome.Known(new PropertyValue.Number(50000))),
            (TreeProperty.Name, new PropertyOutcome.Known(new PropertyValue.Text("Save"))),
            (TreeProperty.AutomationId, new PropertyOutcome.Known(new PropertyValue.Text("save-btn"))),
            (TreeProperty.HelpText, new PropertyOutcome.Known(new PropertyValue.Text("Save document"))),
        ]);

        var candidate = ElementResolve.CandidateIdentity(props, "w1");
        Assert.Equal("button", candidate.Role);
        Assert.Equal("Save", candidate.StableName.Value);
        Assert.Equal("save-btn", candidate.NativeId);
        Assert.Equal("Save document", candidate.Description);
        Assert.True(candidate.MatchesCapture(candidate));
    }
}

