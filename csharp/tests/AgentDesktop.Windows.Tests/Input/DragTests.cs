using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Input;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

/// <summary>The test collection that owns the drag posting seam. The seam is one global static,
/// so every class that substitutes it joins this collection and runs sequentially.</summary>
[CollectionDefinition(Name)]
public class DragSinkCollection : ICollectionFixture<DragSinkFixture>
{
    /// <summary>The name every seam-substituting test class declares.</summary>
    public const string Name = "drag-sink";
}

/// <summary>The fixture that lets classes join the drag seam collection. It carries no state.</summary>
public sealed class DragSinkFixture
{
}

/// <summary>Verifies the drag sequence through the recording seam: duration and drop-delay
/// limits, the deadline preflight that refuses before mouse-down, and the single-release order.</summary>
[Collection(DragSinkCollection.Name)]
public sealed class DragTests : IDisposable
{
    private static readonly Point Origin = new(0, 0);

    private static readonly Point Destination = new(10, 10);

    private readonly List<DragEvent> _recorded = new();

    private readonly Action<DragEvent> _previous;

    public DragTests()
    {
        _previous = DragPost.Sink;
        DragPost.Sink = _recorded.Add;
    }

    public void Dispose() => DragPost.Sink = _previous;

    private static Deadline LongDeadline() => Deadline.After(5_000);

    private void Drag(long? durationMs, long? dropDelayMs, Deadline deadline) =>
        AgentDesktop.Windows.Input.Drag.SynthesizeDrag(Origin, Destination, durationMs, dropDelayMs, deadline);

    private int MovesWhileButtonHeld()
    {
        var down = _recorded.FindIndex(e => e.ButtonDown);
        var up = _recorded.FindIndex(e => e.ButtonUp);
        if (down < 0 || up <= down)
        {
            return 0;
        }

        return _recorded.Skip(down + 1).Take(up - down - 1).Count(e => e.Point is not null);
    }

    [Fact]
    public void DragLimitsRejectUnboundedWorkAndAcceptTheLimitsThemselves()
    {
        var tooLong = Assert.Throws<AdapterError>(() =>
            AgentDesktop.Windows.Input.Drag.ValidateDrag(Origin, Destination, AgentDesktop.Windows.Input.Drag.MaxDragMs + 1, null));
        Assert.Equal(ErrorCode.InvalidArgs, tooLong.Code);

        var tooSlowToRelease = Assert.Throws<AdapterError>(() =>
            AgentDesktop.Windows.Input.Drag.ValidateDrag(Origin, Destination, null, AgentDesktop.Windows.Input.Drag.MaxDropDelayMs + 1));
        Assert.Equal(ErrorCode.InvalidArgs, tooSlowToRelease.Code);

        Assert.Null(Record.Exception(() =>
            AgentDesktop.Windows.Input.Drag.ValidateDrag(Origin, Destination, AgentDesktop.Windows.Input.Drag.MaxDragMs, null)));
        Assert.Null(Record.Exception(() =>
            AgentDesktop.Windows.Input.Drag.ValidateDrag(Origin, Destination, null, AgentDesktop.Windows.Input.Drag.MaxDropDelayMs)));
    }

    [Fact]
    public void ImpossibleDragDeadlineFailsBeforeMouseDown()
    {
        var error = Assert.Throws<AdapterError>(() => Drag(1, 0, Deadline.After(1)));

        Assert.Equal(ErrorCode.Timeout, error.Code);
        Assert.NotNull(error.Details);
        Assert.Contains("\"physical_delivery_started\":false", error.Details);
        Assert.Empty(_recorded);
    }

    [Fact]
    public void AZeroDropDelayPostsNoDwellMovesWhileANonzeroOneDoes()
    {
        Drag(0, 0, LongDeadline());
        var withoutDwell = MovesWhileButtonHeld();

        _recorded.Clear();
        Drag(0, 64, LongDeadline());
        var withDwell = MovesWhileButtonHeld();

        Assert.True(withDwell > withoutDwell,
            $"a dwell must post repeated moves over the destination; got {withoutDwell} without and {withDwell} with");
    }

    [Fact]
    public void ASubTickDurationStillPostsOneInterpolatedMove()
    {
        Drag(1, 0, LongDeadline());

        Assert.Equal(2, MovesWhileButtonHeld());
    }

    [Fact]
    public void ASuccessfulDragPostsDownMoveAndUpWithNoCorrectiveRelease()
    {
        Drag(0, 0, Deadline.After(5_000));

        Assert.True(_recorded[1].ButtonDown, "the move to the origin is immediately followed by the button down");
        Assert.Contains(_recorded, e => e.Point is not null);
        Assert.True(_recorded[^1].ButtonUp, "the sequence ends with the release and nothing after it");
        Assert.Equal(1, _recorded.Count(e => e.ButtonDown));
        Assert.Equal(1, _recorded.Count(e => e.ButtonUp));
    }

    [Fact]
    public void ADeadlineThatCoversOnlyOnePickupDelayIsRefusedBeforeTheButtonLands()
    {
        var deadline = Deadline.After(AgentDesktop.Windows.Input.Drag.PickupDelayMs + 20);

        var error = Assert.Throws<AdapterError>(() => Drag(0, 0, deadline));

        Assert.Equal(ErrorCode.Timeout, error.Code);
        Assert.Contains("\"physical_delivery_started\":false", error.Details);
        Assert.Empty(_recorded);
    }
}
