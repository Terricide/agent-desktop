using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Input;
using Xunit;

namespace AgentDesktop.Windows.Tests.Input;

/// <summary>Verifies the drag release guard: the delivery report it produces and the corrective
/// release it posts at the origin, never the destination.</summary>
[Collection(DragSinkCollection.Name)]
public sealed class DragStateTests : IDisposable
{
    private static readonly Point Origin = new(111, 222);

    private readonly List<DragEvent> _recorded = new();

    private readonly Action<DragEvent> _previous;

    public DragStateTests()
    {
        _previous = DragPost.Sink;
        DragPost.Sink = _recorded.Add;
    }

    public void Dispose() => DragPost.Sink = _previous;

    [Fact]
    public void NeverArmedGuardReportsNotDeliveredAndNoRelease()
    {
        using var guard = new DragReleaseGuard(Origin);
        var error = guard.EnrichError(AdapterError.Internal("pre-post failure"));

        Assert.Equal(DeliverySemantics.NotDelivered().Delivery, error.Disposition.Delivery);
        Assert.False(guard.ShouldRelease());
    }

    [Fact]
    public void ArmedGuardWithDeliveriesReportsDeliveredUnverifiedAndEmergencyRelease()
    {
        using var guard = new DragReleaseGuard(Origin);
        guard.Arm();
        for (var i = 0; i < 4; i++)
        {
            guard.MarkDelivered();
        }

        var error = guard.EnrichError(AdapterError.Timeout("deadline"));

        Assert.Equal(DeliverySemantics.DeliveredUnverified().Delivery, error.Disposition.Delivery);
        Assert.NotNull(error.Details);
        Assert.Contains("\"delivered_events\":4", error.Details);
        Assert.Contains("\"emergency_release_posted\":true", error.Details);
        Assert.Contains("\"emergency_release_acknowledged\":false", error.Details);
    }

    [Fact]
    public void DisarmedGuardPostsNothingOnDispose()
    {
        using (var guard = new DragReleaseGuard(Origin))
        {
            guard.Arm();
            guard.MarkDelivered();
            guard.Disarm();
        }

        Assert.Empty(_recorded);
    }

    [Fact]
    public void ArmedGuardPostsCorrectiveMoveAndReleaseAtOriginOnDispose()
    {
        using (var guard = new DragReleaseGuard(Origin))
        {
            guard.Arm();
            guard.MarkDelivered();
        }

        Assert.Equal(2, _recorded.Count);
        Assert.Equal(Origin, _recorded[0].Point);
        Assert.True(_recorded[1].ButtonUp);
    }
}
