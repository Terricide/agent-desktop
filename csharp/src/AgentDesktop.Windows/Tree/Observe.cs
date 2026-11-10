using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.System;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// Runs one observation: resolves the root, walks it, detects a Chromium shell,
/// and reports completeness honestly.
/// </summary>
/// <remarks>
/// The walk itself already produces honest boundaries: at a logical or raw depth
/// boundary it has already enumerated the full child list (bounded by the sibling
/// and deadline budgets), so <c>ChildrenCount</c> is the real count and
/// <c>SubtreeTruncated</c> records that descendants were not walked. The two flags
/// are set independently (a boundary node may carry a count with
/// <c>SubtreeTruncated</c> true). A deadline-starved walk reports the partial tree
/// it observed with <c>complete: false</c>, never as a discard.
///
/// This class adds what the walk cannot decide for itself:
///
/// 1. Enumeration-failure surfacing: a walk with real faults throws a structured
///    error, never a silently-truncated success.
/// 2. Liveness-checked observation: complete and partial trees are only returned
///    after the root's stored identity re-verifies live.
/// 3. Chromium shell detection: a full-depth walk of a detected Chromium root that
///    lands on the pre-activation shell throws the activation-required error, which
///    core's loop settles and retries. The marker keeps being thrown, re-arming
///    core's retry loop, for as long as the deadline still has room for another walk
///    of the shape just measured. Only once that room runs out does a still-thin
///    tree throw the plain guidance error core's loop treats as final. The
///    "settle already ran" state is scoped per process, so a batch or session
///    observing several processes through one adapter gives every process its own
///    settle rather than the first process's activation suppressing every later one.
///    When the caller already forced renderer accessibility
///    (<c>--force-electron-a11y</c>), the adapter still reverifies the root is live
///    before returning the observed tree instead of the guidance error. The flag's
///    contract is to skip activation guidance, not liveness.
/// </remarks>
internal static class Observe
{
    /// <summary>
    /// The shortest walk estimate used when judging remaining deadline room. A walk
    /// this small can complete near-instantly under a test double.
    /// </summary>
    internal static readonly TimeSpan MinWalkEstimate = TimeSpan.FromMilliseconds(5);

    internal static ObservedTree ObserveTree(
        ObservationRoot root,
        ObservationRequest request,
        WindowsAdapter adapter)
    {
        var validated = request.Validate();
        ReVerifyRoot(root);
        var element = Surfaces.SurfaceRoot(root, validated.Surface, validated.Deadline);
        var chromiumRoot = Chromium.IsChromiumRoot(element);
        var budget = new WalkBudget(validated.MaxLogicalDepth, validated.Deadline)
            .WithMaxRawDepth(Math.Min(validated.MaxRawDepth, WalkBudget.DefaultMaxRawDepth));
        var walkStarted = Stopwatch.StartNew();
        var outcome = Walker.WalkUiaSubtree(element, root, budget, validated.Options.IncludeBounds);
        return FinishObservation(
            root,
            validated,
            adapter,
            new CompletedWalk(chromiumRoot, outcome, walkStarted.Elapsed),
            ReVerifyRoot);
    }

    /// <summary>
    /// Everything the walk cannot decide for itself, applied to an already-completed
    /// walk outcome.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="ObserveTree"/> so the decision logic (the shell/settle
    /// handling and the liveness gate) can be driven directly from a synthetic walk
    /// outcome and a fake liveness check. <see cref="ObserveTree"/> supplies the real
    /// walk and <see cref="ReVerifyRoot"/>; tests supply a canned outcome and an action
    /// that throws on demand.
    /// </remarks>
    internal static ObservedTree FinishObservation(
        ObservationRoot root,
        ObservationRequest request,
        WindowsAdapter adapter,
        CompletedWalk walk,
        Action<ObservationRoot> verifyRoot)
    {
        if (walk.Outcome.Failures.Count > 0)
        {
            throw WalkFaultError(walk.Outcome.Failures);
        }
        verifyRoot(root);
        var shellShaped = walk.Outcome.Tree.IsComplete()
            && walk.ChromiumRoot
            && Chromium.ActivationEligible(root, request)
            && Chromium.IsShellShaped(walk.Outcome.Stats, request);
        if (shellShaped)
        {
            if (request.ObservationMode.ForceRendererAccessibility)
            {
                return walk.Outcome.Tree;
            }
            var process = RootProcessIdentity(root);
            if (!adapter.RendererActivationAttempted(process)
                || HasRoomForAnotherWalk(request.Deadline, walk.WalkDuration))
            {
                throw Chromium.ActivationRequired(walk.Outcome.Stats);
            }
            throw StillThinAfterSettle();
        }
        return walk.Outcome.Tree;
    }

    /// <summary>
    /// Whether the remaining deadline can still absorb another walk of the shape that
    /// was just measured.
    /// </summary>
    /// <remarks>
    /// Core's loop retries <c>ObserveTree</c> on every activation-required error with a
    /// 25-400ms backoff, so reporting the marker for as long as there is room turns the
    /// settle from a single guess into a real wait for the async Chromium accessibility
    /// build a cold start needs. The walk that just ran is the cheapest available
    /// estimate of what another attempt costs. The estimate is floored at
    /// <see cref="MinWalkEstimate"/>, because a deadline this thin is not enough runway
    /// for a genuine retry whatever the last walk measured.
    /// </remarks>
    internal static bool HasRoomForAnotherWalk(Deadline deadline, TimeSpan lastWalk)
    {
        return deadline.Remaining() > TimeSpan.FromTicks(Math.Max(lastWalk.Ticks, MinWalkEstimate.Ticks));
    }

    /// <summary>
    /// The activation-attempted key for a root: its process, scoped by the
    /// process-instance token so a recycled pid never inherits another process's
    /// settle state.
    /// </summary>
    internal static ProcessIdentity RootProcessIdentity(ObservationRoot root)
    {
        return root switch
        {
            ObservationRoot.Application application => new ProcessIdentity(
                application.App.Pid,
                application.App.ProcessInstance ?? string.Empty),
            ObservationRoot.Window window => new ProcessIdentity(
                window.Info.Pid,
                window.Info.ProcessInstance ?? string.Empty),
            ObservationRoot.Element element => new ProcessIdentity(
                element.Entry.Process.Pid,
                element.Entry.Process.Instance ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(root)),
        };
    }

    /// <summary>
    /// The post-settle still-thin error. It is deliberately not marked
    /// activation-required, so it escapes core's loop and reaches the caller. A
    /// Chromium tree that genuinely stays thin after the async build has the guidance
    /// <c>platform_detail</c> and no target-derived text.
    /// </summary>
    internal static AdapterError StillThinAfterSettle()
    {
        return new AdapterError(
                ErrorCode.ActionFailed,
                "The Chromium tree is still thin after its accessibility build settled")
            .WithPlatformDetail(Chromium.StillThinDetail());
    }

    /// <summary>
    /// Rechecks stored ownership and generation evidence after the UIA read.
    /// </summary>
    internal static void ReVerifyRoot(ObservationRoot root)
    {
        switch (root)
        {
            case ObservationRoot.Application:
                throw AdapterError.NotSupported("application observation root");
            case ObservationRoot.Window window:
                var handle = HwndOf(window.Info.Id);
                var evidence = WindowIdentityEvidence.FromInfo((nint)handle, window.Info)
                    ?? throw WindowIdentityChanged();
                evidence.VerifyStored();
                return;
            case ObservationRoot.Element element:
                VerifyProcessIdentity(
                    element.Entry.Process.Pid,
                    element.Entry.Process.Instance);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(root));
        }
    }

    /// <summary>
    /// Corroborates a root's process against its stored generation token, fail-closed.
    /// A stored ref without a token can never be corroborated, so a missing token never
    /// passes. It is evidence that must be present, not an optional extra.
    /// </summary>
    internal static void VerifyProcessIdentity(ProcessId pid, string? instance)
    {
        if (instance is null)
        {
            throw WindowIdentityChanged();
        }
        if (!ProcessIdentityCheck.MatchesInstance(pid, instance))
        {
            throw WindowIdentityChanged();
        }
    }

    internal static nint HwndOf(string id)
    {
        if (!id.StartsWith("w-", StringComparison.Ordinal))
        {
            throw MalformedWindowIdentifier();
        }
        if (!long.TryParse(
                id.AsSpan(2),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var handle)
            || handle <= 0)
        {
            throw MalformedWindowIdentifier();
        }
        return (nint)handle;
    }

    internal static AdapterError MalformedWindowIdentifier()
    {
        return new AdapterError(ErrorCode.InvalidArgs, "Malformed window identifier");
    }

    internal static AdapterError WindowIdentityChanged()
    {
        return new AdapterError(
                ErrorCode.WindowNotFound,
                "The window's process changed during observation")
            .WithSuggestion("Run 'list-windows' to refresh window IDs, then retry.");
    }

    /// <summary>
    /// Builds the structured error a walk with enumeration faults returns.
    /// </summary>
    /// <remarks>
    /// Shape only: each fault's kind, axis, depth, and child index, never an application
    /// name or property value, because this error reaches trace segments.
    /// </remarks>
    internal static AdapterError WalkFaultError(IReadOnlyList<AdapterError> failures)
    {
        var faults = new JsonArray();
        foreach (var failure in failures)
        {
            var fault = failure.Details is null
                ? new JsonObject { ["kind"] = "walk_fault" }
                : JsonNode.Parse(failure.Details);
            faults.Add(fault);
        }
        return new AdapterError(
                ErrorCode.ActionFailed,
                "Element tree walk encountered an enumeration failure")
            .WithDetails(new JsonObject { ["faults"] = faults });
    }

    /// <summary>
    /// The walk's own output, grouped so <see cref="FinishObservation"/> stays within the
    /// project's parameter limit. The shell/settle decision reads all three together, and
    /// none of them means anything without the others.
    /// </summary>
    internal sealed record CompletedWalk(bool ChromiumRoot, WalkOutcome Outcome, TimeSpan WalkDuration);
}
