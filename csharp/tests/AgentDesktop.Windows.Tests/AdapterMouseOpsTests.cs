using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input.Mouse;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>
/// A standalone pointer event posts the mouse inputs its kind and button name, and reports the shared
/// core's unverified dispatch. The report is what the CLI prints, so the labels and mechanism the step
/// carries are part of the contract: a caller reads the delivery from them.
///
/// The wheel is the part with a rule of its own: the caller states the delta in pixels, and Windows posts
/// notches. The conversion must never round a stated delta to zero, because a post that moves nothing
/// looks like a delivery.
/// </summary>
public class AdapterMouseOpsTests : IDisposable
{
    private readonly List<MouseInputEvent> _posted = new List<MouseInputEvent>();
    private readonly Action<IReadOnlyList<MouseInputEvent>> _originalSink;

    public AdapterMouseOpsTests()
    {
        _originalSink = MouseSend.PostSink;
        MouseSend.PostSink = _posted.AddRange;
    }

    public void Dispose() => MouseSend.PostSink = _originalSink;

    private static MouseEvent Move(double x, double y) =>
        new MouseEvent { Kind = "move", Point = new Point(x, y) };

    private static MouseEvent Click(string button = "left") =>
        new MouseEvent { Kind = "click", Point = new Point(100, 200), Button = button };

    private static MouseEvent Wheel(double deltaY, double deltaX = 0) =>
        new MouseEvent { Kind = "wheel", Point = new Point(100, 200), DeltaY = deltaY, DeltaX = deltaX };

    private static readonly InteractionLease Lease =
        new(Deadline.Standard(), InteractionPolicy.Physical);

    [Fact]
    public void AStandalonePointerEventReportsTheUnverifiedDispatchItNamed()
    {
        OperationReport report = AdapterMouseOps.MouseEvent(Move(400, 300), Lease);

        Assert.Equal("mouse-move", report.Result.Action);
        Assert.Equal("delivered_unverified", report.Disposition().Delivery.AsString());
        Assert.Single(report.Result.Steps);
        Assert.Equal("pointer_move", report.Result.Steps[0].Label);
        Assert.Equal("physical_synthetic", report.Result.Steps[0].Mechanism);
    }

    [Fact]
    public void APointInsideThePrimaryRectIsPostedAsAFractionOfTheNormalizedRangeNotRawPixels()
    {
        AdapterMouseOps.MouseEvent(Move(1200, 700), Lease);

        MouseInputEvent move = Assert.Single(_posted);
        Assert.Equal(0x8001, (int)move.Flags);
        Assert.True(move.Dx > 10000, $"dx={move.Dx} is a raw pixel, not a normalized position");
        Assert.True(move.Dy > 10000, $"dy={move.Dy} is a raw pixel, not a normalized position");
    }

    [Fact]
    public void APointFarOutsideTheScreenIsNormalizedAgainstTheVirtualScreen()
    {
        AdapterMouseOps.MouseEvent(Move(100000, 10), Lease);

        MouseInputEvent move = Assert.Single(_posted);
        Assert.Equal(0xC001, (int)move.Flags);
        Assert.Equal(65535, move.Dx);
    }

    [Fact]
    public void EachPointerKindNamesTheActionItsCommandPrints()
    {
        Assert.Equal("mouse-down", AdapterMouseOps.MouseEvent(
            new MouseEvent { Kind = "down", Point = new Point(1, 2) }, Lease).Result.Action);
        Assert.Equal("mouse-up", AdapterMouseOps.MouseEvent(
            new MouseEvent { Kind = "up", Point = new Point(1, 2) }, Lease).Result.Action);
        Assert.Equal("mouse-wheel", AdapterMouseOps.MouseEvent(Wheel(120), Lease).Result.Action);
        Assert.Equal("mouse-click", AdapterMouseOps.MouseEvent(Click(), Lease).Result.Action);
    }

    [Fact]
    public void AWheelDeltaSmallerThanOneNotchStillPostsOneNotch()
    {
        Assert.Equal(1, AdapterMouseOps.Notches(60));
        Assert.Equal(-1, AdapterMouseOps.Notches(-60));
        Assert.Equal(0, AdapterMouseOps.Notches(0));
        Assert.Equal(2, AdapterMouseOps.Notches(240));
    }

    [Fact]
    public void AHorizontalWheelDeltaAddsTheHorizontalFlagAndCarriesItsCount()
    {
        MouseInputEvent wheel = AdapterMouseOps.WheelEventFor(Wheel(0, -120));

        Assert.Equal(0, wheel.Dy);
        Assert.Equal(-1, wheel.MouseData);
        Assert.Equal(0x1800, (int)wheel.Flags);
    }

    [Fact]
    public void AVerticalOnlyWheelCarriesTheVerticalFlagAlone()
    {
        Assert.Equal(0x0800, (int)AdapterMouseOps.WheelEventFor(Wheel(120)).Flags);
    }

    [Fact]
    public void AReleaseBitIsChosenFromThePressBitTheButtonNames()
    {
        Assert.Equal(0x0004, (int)AdapterMouseOps.ReleaseFor(0x0002));
        Assert.Equal(0x0010, (int)AdapterMouseOps.ReleaseFor(0x0008));
        Assert.Equal(0x0040, (int)AdapterMouseOps.ReleaseFor(0x0020));
    }

    [Fact]
    public void TheDownAndUpOfAClickAreThePairForTheNamedButton()
    {
        Assert.Equal(0x0008, (int)AdapterMouseOps.PressFor("right"));
        Assert.Equal(0x0020, (int)AdapterMouseOps.PressFor("middle"));
        Assert.Equal(0x0002, (int)AdapterMouseOps.PressFor("left"));
    }
}
