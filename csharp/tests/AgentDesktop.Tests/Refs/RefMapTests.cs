using AgentDesktop.Core.Refs;
using Xunit;

namespace AgentDesktop.Tests.Refs;

public class RefMapTests
{
    private static RefEntryIdentity Identity(string role) =>
        new() { Role = role, WindowKey = "w1" };

    [Fact]
    public void RefsAreAllocatedInCaptureOrder()
    {
        var map = new RefMap("s8f3k2p9");
        Assert.Equal("e1", map.Add(new RefProcess(), Identity("Button"), new RefGeometry()));
        Assert.Equal("e2", map.Add(new RefProcess(), Identity("CheckBox"), new RefGeometry()));
        Assert.Equal("e3", map.Add(new RefProcess(), Identity("Slider"), new RefGeometry()));
    }

    [Fact]
    public void OnlyTheSnapshotQualifiedFormIsAccepted()
    {
        var map = new RefMap("s8f3k2p9");
        map.Add(new RefProcess(), Identity("Button"), new RefGeometry());
        Assert.NotNull(map.Find("@s8f3k2p9:e1"));
        Assert.Null(map.Find("@e1"));
    }

    [Fact]
    public void ARefFromAnotherSnapshotDoesNotResolve()
    {
        var map = new RefMap("s8f3k2p9");
        map.Add(new RefProcess(), Identity("Button"), new RefGeometry());
        Assert.Null(map.Find("@other:e1"));
    }
}
