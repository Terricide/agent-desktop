using AgentDesktop.Core.Refs;
using Xunit;

namespace AgentDesktop.Tests.Refs;

public class RefMapTests
{
    private static RefEntry Entry(string role) => new()
    {
        Identity = new RefEntryIdentity { Role = role, WindowKey = "w1" },
        Capabilities = new RefCapabilities { UniqueAtCapture = true },
    };

    [Fact]
    public void RefsAreAllocatedInCaptureOrder()
    {
        var map = new RefMap("s8f3k2p9");
        Assert.Equal("e1", map.Add(Entry("Button")));
        Assert.Equal("e2", map.Add(Entry("CheckBox")));
        Assert.Equal("e3", map.Add(Entry("Slider")));
    }

    [Fact]
    public void OnlyTheSnapshotQualifiedFormIsAccepted()
    {
        var map = new RefMap("s8f3k2p9");
        map.Add(Entry("Button"));
        Assert.NotNull(map.Find("@s8f3k2p9:e1"));
        Assert.Null(map.Find("@e1"));
    }

    [Fact]
    public void ARefFromAnotherSnapshotDoesNotResolve()
    {
        var map = new RefMap("s8f3k2p9");
        map.Add(Entry("Button"));
        Assert.Null(map.Find("@other:e1"));
    }
}
