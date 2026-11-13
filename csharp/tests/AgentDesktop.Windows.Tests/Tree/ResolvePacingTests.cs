using AgentDesktop.Windows.Tree;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>The pause doubles so a provider that is genuinely struggling is not polled at the
/// opening rate for the whole budget, and it stops doubling so a long deadline cannot stretch
/// one pause past the point of usefulness.</summary>
public sealed class ResolvePacingTests
{
    [Fact]
    public void The_retry_pause_doubles_up_to_its_ceiling_and_stays_there()
    {
        var first = ResolvePacing.FirstRetryPause;
        var second = ResolvePacing.NextRetryPause(first);
        Assert.Equal(first + first, second);

        var pause = first;
        for (var i = 0; i < 12; i++)
        {
            pause = ResolvePacing.NextRetryPause(pause);
        }

        Assert.Equal(ResolvePacing.MaximumRetryPause, pause);
        Assert.Equal(ResolvePacing.MaximumRetryPause, ResolvePacing.NextRetryPause(pause));
    }
}
