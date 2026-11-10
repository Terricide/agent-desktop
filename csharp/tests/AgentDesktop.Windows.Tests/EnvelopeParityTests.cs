using System.Text.Json.Nodes;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Pins the pointer and drag OperationReport wire shape against the Rust
/// OperationReport::pointer_dispatched and OperationReport::drag_dispatched contract: the action
/// name per event kind, the physical_synthetic mechanism, the verification scope, and the target.</summary>
public sealed class EnvelopeParityTests
{
    private static MouseEvent Event(string kind) => new() { Kind = kind, Point = new Point(3, 4), ClickCount = 1 };

    [Fact]
    public void UnverifiedPointerDispatchNamesTheEventAndClaimsNoProof()
    {
        var report = OperationReportBuild.PointerDispatched(Event("click"), false);

        Assert.Equal("mouse-click", report.Result.Action);
        Assert.Equal(DeliverySemantics.DeliveredUnverified(), report.Disposition());
        Assert.Null(report.Result.Steps[0].Verified);
        Assert.Null(report.Result.Details);
    }

    [Fact]
    public void VerifiedPointerMoveScopesTheProofToPointerPosition()
    {
        var report = OperationReportBuild.PointerDispatched(Event("move"), true);

        Assert.Equal(DeliverySemantics.DeliveredVerified(), report.Disposition());
        Assert.Equal(true, report.Result.Steps[0].Verified);
        Assert.NotNull(report.Result.Details);
        Assert.Equal("pointer_position", report.Result.Details["verification_scope"]?.GetValue<string>());
    }

    [Fact]
    public void DragReportCarriesBothEndpointsAndStaysUnverified()
    {
        var report = OperationReportBuild.DragDispatched(new DragParams
        {
            From = new Point(0, 0),
            To = new Point(9, 9),
        });

        Assert.Equal(DeliverySemantics.DeliveredUnverified(), report.Disposition());
        Assert.Equal("drag", report.Target.Kind);
        Assert.Equal(new Point(0, 0), report.Target.From);
        Assert.Equal(new Point(9, 9), report.Target.To);
    }

    [Theory]
    [InlineData("move", "mouse-move", "pointer_move")]
    [InlineData("down", "mouse-down", "pointer_down")]
    [InlineData("up", "mouse-up", "pointer_up")]
    [InlineData("click", "mouse-click", "pointer_click")]
    [InlineData("wheel", "mouse-wheel", "pointer_wheel")]
    public void EachPointerKindNamesItsActionAndStepLabel(string kind, string action, string label)
    {
        var report = OperationReportBuild.PointerDispatched(Event(kind), false);

        Assert.Equal(action, report.Result.Action);
        Assert.Equal(label, report.Result.Steps[0].Label);
        Assert.Equal("succeeded", report.Result.Steps[0].Outcome);
        Assert.Equal("physical_synthetic", report.Result.Steps[0].Mechanism);
        Assert.Equal("pointer", report.Target.Kind);
        Assert.Equal(new Point(3, 4), report.Target.Pointer);
    }

    [Fact]
    public void UnknownPointerKindIsRefusedBeforeAnyDispatch()
    {
        var error = Assert.Throws<AdapterError>(() => OperationReportBuild.PointerDispatched(Event("tap"), false));

        Assert.Equal(ErrorCode.InvalidArgs, error.Code);
        Assert.Equal(DeliverySemantics.NotDelivered(), error.Disposition);
    }

    [Fact]
    public void DragStepIsPhysicalAndUnverified()
    {
        var report = OperationReportBuild.DragDispatched(new DragParams { From = new Point(1, 1), To = new Point(2, 2) });

        Assert.Equal("pointer_drag", report.Result.Steps[0].Label);
        Assert.Equal("physical_synthetic", report.Result.Steps[0].Mechanism);
        Assert.Null(report.Result.Steps[0].Verified);
        Assert.Null(report.Result.Details);
    }
}
