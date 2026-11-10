using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>Verifies the Windows keyboard delivery gates: process generation, injection integrity,
/// foreground ownership, keyboard focus, and the final foreground read before synthesis. Every gate must
/// stop the delivery before any physical key event exists, so each test asserts both the refusal and that
/// the synthesis probe never fired.</summary>
/// <remarks>SEAM: crates/windows/src/system/key_dispatch_tests.rs - the live foreground directions
/// (a headed foreground target synthesizes and reports delivered_unverified, a headless foreground target
/// verifies without stealing, and the adapter wiring through system ops) need the hosted fixture and the
/// foreground staging path, which are not ported to C#, so no target can be made foreground to deliver to.
/// SEAM: crates/windows/src/system/key_dispatch_focus_tests.rs - the keyboard-focus timeout with a staged
/// foreground target needs the same fixture; the portable half, a target that never holds the foreground,
/// is asserted here.
/// SEAM: crates/windows/src/system/key_dispatch_tests.rs - the FFI default for PressKey is asserted in the
/// Rust tree against the C ABI sources, which have no C# counterpart.</remarks>
public sealed class KeyDispatchTests
{
    private const uint HigherIntegrityRid = 0x2000;
    private const uint LowerIntegrityRid = 0x3000;
    private const long DeliveryBudgetMs = 10_000;

    private static KeyCombo ComboA() => new KeyCombo { Key = "a", Modifiers = new List<Modifier>() };

    /// <summary>The generation token of this test process. No fixture is needed: every gate that refuses
    /// before synthesis runs on a live process that never holds the foreground.</summary>
    private static ProcessIdentity CurrentIdentity()
    {
        var pid = (uint)Process.GetCurrentProcess().Id;
        var token = ProcessTimes.TokenForPid(pid);
        Assert.NotNull(token);
        return new ProcessIdentity(new ProcessId(pid), token!);
    }

    /// <summary>A token for this process whose generation is deliberately wrong: the shape a recycled
    /// process id on a different generation carries.</summary>
    private static ProcessIdentity CorruptedIdentity()
    {
        var token = ProcessTimes.TokenForPid((uint)Process.GetCurrentProcess().Id)!;
        var parts = token.Split(':');
        var seconds = ulong.Parse(parts[1]) + 1;
        return new ProcessIdentity(new ProcessId((uint)Process.GetCurrentProcess().Id), $"{parts[0]}:{seconds}:{parts[2]}");
    }

    private static AdapterError Press(ProcessIdentity identity, InteractionPolicy policy) =>
        Assert.Throws<AdapterError>(
            () => KeyDispatch.PressForApp(identity, ComboA(), policy, Deadline.After(DeliveryBudgetMs)));

    /// <summary>Arms the focus fault for the read after the verify-and-inject seam, and clears it when the
    /// press returns, so a later test on this thread reads the real desktop.</summary>
    private static AdapterError PressWithFocusArmed(InteractionPolicy policy) =>
        KeyDispatchHooks.ForceFocusLost.WithArmedAfterHook(
            KeyDispatchHooks.ForceFocusLost.Arm,
            () => Press(CurrentIdentity(), policy));

    private static void AssertNotDelivered(AdapterError error)
    {
        Assert.Equal(Delivery.NotDelivered, error.Disposition.Delivery);
        Assert.False(JsonNode.Parse(error.Details!)!["physical_delivery_started"]!.GetValue<bool>());
    }

    private static uint SynthesisCount() => KeyDispatchHooks.SynthesisProbe.Count();

    [Fact]
    public void HigherIntegrityIsPermDeniedNotDeliveredBeforeSynthesis()
    {
        KeyDispatchHooks.SynthesisProbe.Reset();

        var error = KeyDispatchHooks.ForceHigherIntegrity.With(
            () => Press(CurrentIdentity(), InteractionPolicy.Physical));

        Assert.Equal(ErrorCode.PermDenied, error.Code);
        AssertNotDelivered(error);
        Assert.Equal(0u, SynthesisCount());
    }

    [Fact]
    public void RecycledProcessGenerationFailsClosedBeforeSynthesis()
    {
        KeyDispatchHooks.SynthesisProbe.Reset();

        var error = Press(CorruptedIdentity(), InteractionPolicy.Physical);

        Assert.Equal(ErrorCode.AppUnresponsive, error.Code);
        AssertNotDelivered(error);
        Assert.Equal(0u, SynthesisCount());
    }

    [Fact]
    public void UnspecifiedPolicyBackgroundTargetFailsClosedNamingTheHeadedRetry()
    {
        KeyDispatchHooks.SynthesisProbe.Reset();

        var error = PressWithFocusArmed(InteractionPolicy.Unspecified);

        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        AssertNotDelivered(error);
        Assert.Contains("background", error.Message);
        Assert.Contains("--headed", error.Suggestion!);
        Assert.Equal(0u, SynthesisCount());
    }

    [Fact]
    public void SyntheticPolicyBackgroundTargetFailsClosedWithoutActivation()
    {
        KeyDispatchHooks.SynthesisProbe.Reset();

        var error = PressWithFocusArmed(InteractionPolicy.Synthetic);

        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        AssertNotDelivered(error);
        Assert.Equal(0u, SynthesisCount());
    }

    /// <summary>Physical is the only policy the Windows keyboard legs authorize to take focus, so it is the
    /// policy whose refusal names the frontmost retry. The refusal must not name an activation call: this
    /// path verifies focus and never raises a window.</summary>
    [Fact]
    public void PhysicalPolicyRefusalNamesTheFrontmostRetry()
    {
        KeyDispatchHooks.SynthesisProbe.Reset();

        var error = PressWithFocusArmed(InteractionPolicy.Physical);

        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        AssertNotDelivered(error);
        Assert.Contains("focus", error.Message);
        Assert.Contains("frontmost", error.Suggestion!);
        Assert.Equal(0u, SynthesisCount());
    }

    [Fact]
    public void KeyboardFocusDeniedFailsClosedWithoutSynthesis()
    {
        KeyDispatchHooks.SynthesisProbe.Reset();

        var error = KeyDispatchHooks.ForceKeyboardFocusDenied.With(
            () => Press(CurrentIdentity(), InteractionPolicy.Synthetic));

        Assert.Equal(ErrorCode.ActionFailed, error.Code);
        AssertNotDelivered(error);
        Assert.Equal(0u, SynthesisCount());
    }

    /// <summary>The verify-only path must never raise the target: the activation calls belong to the
    /// staging helper the tests use, not to delivery.</summary>
    [Fact]
    public void KeyDispatchNeverCallsActivationApis()
    {
        var source = File.ReadAllText(DispatchSourcePath());

        Assert.DoesNotContain("WindowActivate", source);
        Assert.DoesNotContain("FocusWindow", source);
        Assert.DoesNotContain("SetForegroundWindow", source);
    }

    private static string DispatchSourcePath() =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(CallerPath())!,
            "..", "..", "..", "src", "AgentDesktop.Windows", "System", "KeyDispatch.cs"));

    private static string CallerPath([CallerFilePath] string path = "") => path;
}
