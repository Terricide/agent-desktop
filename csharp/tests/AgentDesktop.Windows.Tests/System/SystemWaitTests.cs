using System.Diagnostics;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Verifies the Windows menu wait loop: the identity re-check before every predicate read, the
/// double-check before success, the absorption of a predicate timeout, the propagation of a non-timeout
/// predicate error, the exit confirmation on the process handle, and the direction-specific deadline
/// message. Every test drives the shipped loop through <see cref="SystemWait.WaitForMenu"/>, so the
/// assertions are about what the loop executes, not about a hand-rolled predicate stub.</summary>
/// <remarks>SEAM: crates/windows/src/system/wait_tests.rs - the live menu directions
/// (open-true satisfying after a fixture opens its menu, open-false satisfying after a fixture dismisses
/// an open menu, and the poll-count spin guard over a fixed timeout) need the Windows tree fixture
/// harness, which is not ported to C#, so no menu fixture exists to transition.
/// SEAM: crates/windows/src/system/wait_menu_command_live_tests.rs - the command-level live wait, which
/// needs the same fixture harness.
/// SEAM: crates/windows/src/system/wait_exit_confirmation_tests.rs and
/// crates/windows/src/system/wait_expiry_detail_tests.rs - the Rust tests call
/// <c>settle_predicate_error</c> and <c>expiry_detail</c> directly. Both are private in
/// <c>SystemWait.cs</c>, so the same properties are asserted through the public entry point.</remarks>
public sealed class SystemWaitTests
{
    private const uint DeadTargetPid = 4321;
    private const long LongBudgetMs = 10_000;
    private const long ShortBudgetMs = 400;

    /// <summary>The generation token of this test process, the target every hook-driven test reads
    /// against. No fixture is needed: the loop's identity and error branches are exercised on a live
    /// process that owns no menu.</summary>
    private static ProcessIdentity CurrentIdentity()
    {
        var pid = (uint)Process.GetCurrentProcess().Id;
        var token = ProcessTimes.TokenForPid(pid);
        Assert.NotNull(token);
        return new ProcessIdentity(new ProcessId(pid), token!);
    }

    private static AdapterError CatchWait(ProcessIdentity identity, bool open, Deadline deadline) =>
        Assert.Throws<AdapterError>(() => SystemWait.WaitForMenu(identity, open, deadline));

    [Fact]
    public void AlreadyClosedStateSatisfiesTheFirstReadWithOnePoll()
    {
        var identity = CurrentIdentity();
        SystemWait.Hooks.TakePollCalls();

        SystemWait.WaitForMenu(identity, false, Deadline.After(5_000));

        Assert.Equal(1, SystemWait.Hooks.TakePollCalls());
    }

    [Fact]
    public void NonTimeoutPredicateErrorPropagatesOnTheFirstPollWithoutSpinning()
    {
        var identity = CurrentIdentity();
        SystemWait.Hooks.TakePollCalls();
        SystemWait.Hooks.ArmPredicateError(
            new AdapterError(ErrorCode.AppUnresponsive, "forced test predicate failure"));
        var started = Stopwatch.GetTimestamp();

        var error = CatchWait(identity, true, Deadline.After(LongBudgetMs));

        Assert.Equal(ErrorCode.AppUnresponsive, error.Code);
        Assert.Equal(1, SystemWait.Hooks.TakePollCalls());
        Assert.True(
            Stopwatch.GetTimestamp() - started < LongBudgetMs * Stopwatch.Frequency / 1000,
            "a swallowed error would spin toward the 10s deadline instead of returning after one poll");
    }

    [Fact]
    public void EvaluationErrorKeepsThePredicateErrorWhenTheTargetIsAlive()
    {
        var identity = CurrentIdentity();
        SystemWait.Hooks.ArmPredicateError(
            new AdapterError(ErrorCode.AppUnresponsive, "The target process could not be found"));

        var error = CatchWait(identity, true, Deadline.After(LongBudgetMs));

        Assert.Equal(ErrorCode.AppUnresponsive, error.Code);
        Assert.Equal("The target process could not be found", error.Message);
    }

    [Fact]
    public void EvaluationErrorSurfacesAsStaleRefWhenTheTargetIsGone()
    {
        var identity = CurrentIdentity();
        SystemWait.Hooks.ArmProcessDeathAfter(1);
        SystemWait.Hooks.ArmPredicateError(
            new AdapterError(ErrorCode.AppUnresponsive, "The target process could not be found"));

        var error = CatchWait(identity, true, Deadline.After(LongBudgetMs));

        Assert.Equal(ErrorCode.StaleRef, error.Code);
    }

    /// <summary>The flaky teardown staged without racing a real termination: the loop-top check and the
    /// in-arm liveness re-check both read the target as alive, and only the exit confirmation on the
    /// process handle, the third consultation, sees it gone. A regression back to a single one-shot
    /// liveness check answers AppUnresponsive here and never consults that third check.</summary>
    [Fact]
    public void TargetThatExitsAfterTheLivenessRechecksSurfacesAsStaleRef()
    {
        var identity = CurrentIdentity();
        SystemWait.Hooks.ArmProcessDeathAfter(2);
        SystemWait.Hooks.ArmPredicateError(
            new AdapterError(ErrorCode.AppUnresponsive, "menu_probe_window_not_pumping"));

        var error = CatchWait(identity, true, Deadline.After(LongBudgetMs));

        Assert.Equal(ErrorCode.StaleRef, error.Code);
    }

    /// <summary>The confirmation window is capped by the caller's deadline, so an already spent budget
    /// cannot be stretched by the confirmation slice.</summary>
    [Fact]
    public void ExitConfirmationNeverOutlastsASpentDeadline()
    {
        var identity = CurrentIdentity();
        var deadline = Deadline.After(1);
        Thread.Sleep(20);
        SystemWait.Hooks.ArmPredicateError(
            new AdapterError(ErrorCode.AppUnresponsive, "still alive"));
        var started = Stopwatch.GetTimestamp();

        var error = CatchWait(identity, true, deadline);

        Assert.Equal(ErrorCode.AppUnresponsive, error.Code);
        Assert.True(
            Stopwatch.GetTimestamp() - started < Stopwatch.Frequency / 2,
            "a spent deadline must not wait out the one-second confirmation slice");
    }

    [Fact]
    public void TimeoutNamesTheOpenDirectionWhenTheStateWasRead()
    {
        var identity = CurrentIdentity();

        var error = CatchWait(identity, true, Deadline.After(ShortBudgetMs));

        Assert.Equal(ErrorCode.Timeout, error.Code);
        Assert.Equal("No menu opened before the deadline", error.PlatformDetail);
    }

    /// <summary>A target that died mid-wait is a fully understood outcome at the moment this error is
    /// built, so it carries the disposition and recovery an agent acts on. The envelope contract says a
    /// consumer may read recovery only when the retry disposition is safe, which a defaulted unknown
    /// never is. The recovery must be process-oriented: this surface has no refs or snapshots to
    /// refresh.</summary>
    [Fact]
    public void DeadTargetReportsNotDeliveredWithProcessOrientedRecovery()
    {
        var identity = new ProcessIdentity(new ProcessId(DeadTargetPid), "gone");

        var error = CatchWait(identity, true, Deadline.After(ShortBudgetMs));

        Assert.Equal(ErrorCode.StaleRef, error.Code);
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
        Assert.NotNull(error.Suggestion);
        Assert.DoesNotContain("  ", error.Suggestion!);
        Assert.DoesNotContain("snapshot", error.Suggestion!);
        Assert.DoesNotContain("ref", error.Suggestion!);
        var details = JsonNode.Parse(error.Details!);
        Assert.Equal(DeadTargetPid, details!["pid"]!.GetValue<uint>());
        Assert.Equal("gone", details["process_instance"]!.GetValue<string>());
    }
}
