using System.Diagnostics;
using AgentDesktop.Core.Actions;
using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.System.CursorOverlay;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>
/// Pins the surface host loop against the Rust surface_host contract: the idle
/// tick is a quarter of the rest budget, a newer control gives way to the
/// reveal, a click flourish plays to its end when nothing interrupts, a caret
/// follow is ignored while the overlay is resting, and a Present with no label
/// draws through the shared paint path.
/// </summary>
public sealed class SurfaceHostTests
{
    private static SurfaceHost Host(string session) => SurfaceHost.Create(session);

    private static CursorOverlayControl Greeting(string session) =>
        CursorOverlayControl.Enable(session, new CursorOverlayStyle());

    private static CursorOverlayControl Flourish(string session, Point destination) =>
        new()
        {
            Action = CursorOverlayControlAction.Present,
            SessionId = session,
            Instruction = new CursorOverlayInstruction
            {
                Destination = destination,
                Phase = CursorPhase.Effect,
                IsClick = true,
            },
        };

    private static double ElapsedMs(long started) =>
        (double)(Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;

    [Fact]
    public void IdleTickIsAQuarterOfTheRestBudget()
    {
        Assert.Equal(1500.0, Host("idle-tick").IdleTick().TotalMilliseconds);
    }

    [Fact]
    public void IdleTickIsTheSameForEverySession()
    {
        Assert.Equal(Host("idle-a").IdleTick(), Host("idle-b").IdleTick());
    }

    [Fact]
    public void AClickFlourishGivesWayToANewerControl()
    {
        using var host = Host("flourish-interrupt");
        var control = Flourish("flourish-interrupt", new Point(120, 120));
        host.Apply(control);

        var started = Stopwatch.GetTimestamp();
        host.Settle(control, () => true);

        Assert.True(ElapsedMs(started) < 100);
    }

    [Fact]
    public void AClickFlourishPlaysToItsEndWhenNothingInterrupts()
    {
        using var host = Host("flourish-complete");
        var control = Flourish("flourish-complete", new Point(120, 120));
        host.Apply(control);

        var started = Stopwatch.GetTimestamp();
        host.Settle(control, () => false);

        Assert.True(ElapsedMs(started) >= 900);
    }

    [Fact]
    public void APresentWithoutALabelDrawsThroughThePaintPath()
    {
        using var host = Host("present-no-label");
        var control = new CursorOverlayControl
        {
            Action = CursorOverlayControlAction.Present,
            SessionId = "present-no-label",
            Instruction = new CursorOverlayInstruction
            {
                Destination = new Point(140, 140),
                Phase = CursorPhase.Effect,
            },
        };

        host.Apply(control);
        host.Settle(control, () => false);

        Assert.Equal(1.0, host.CardOpacity());
    }

    [Fact]
    public void RestBeforeTheQuietBudgetLeavesTheOverlayStanding()
    {
        using var host = Host("rest-early");

        var started = Stopwatch.GetTimestamp();
        host.Rest();

        Assert.True(ElapsedMs(started) < 156);
        Assert.Equal(1.0, host.CardOpacity());
    }

    [Fact]
    public void RestAfterTheQuietBudgetFadesTheOverlayAway()
    {
        using var host = Host("rest-quiet");
        Thread.Sleep((int)(CursorOverlayTiming.IdleRestMs + 100));

        var started = Stopwatch.GetTimestamp();
        host.Rest();

        Assert.True(ElapsedMs(started) >= 156);
    }

    /// <summary>The label is drawn by GDI into the overlay surface, which is not ported, so a control that
    /// asks for one must be refused. The refusal has to land before any presentation state moves: a card
    /// that eased partway in while the text path refused would leave an overlay on screen that can never
    /// show its label. At rest the reveal is settled, so the untouched value is full opacity.</summary>
    [Fact]
    public void DrawingALabelReportsThatTheTextPathIsNotPorted()
    {
        using var host = Host("label-draw");
        var control = Greeting("label-draw");

        var error = Assert.Throws<AdapterError>(() => host.Apply(control));

        Assert.Equal(ErrorCode.PlatformNotSupported, error.Code);
        Assert.Equal(1.0, host.CardOpacity());
    }

    [Fact]
    public void SessionFinishedReportsThatTheManifestReadIsNotPorted()
    {
        using var host = Host("session-finished");

        var error = Assert.Throws<AdapterError>(() => host.SessionFinished());

        Assert.Equal(ErrorCode.PlatformNotSupported, error.Code);
    }
}
