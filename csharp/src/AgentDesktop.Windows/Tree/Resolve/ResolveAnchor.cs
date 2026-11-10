using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree.Resolve;

/// <summary>
/// The locator-anchor resolver: the path-only resolver core's selected-match hydration and stored-ref
/// resolution both land on.
///
/// Core's live locator hydrate re-observes the matched subtree from the return handle and enforces evidence
/// completeness itself; Windows only has to land the anchor. The anchor's path is exact from the just-walked
/// tree, so there is no broad-search fallback: every step is descent along a stored path that churn makes
/// permanently wrong, so a path step that lands nowhere, a role-refuted candidate, or a vanished node settles
/// the attempt immediately (a completed-search STALE_REF, one attempt, the deadline intact for core's fresh
/// re-observation). Only the unresponsive/transport class runs through the bounded retry loop, which re-walks
/// the same cheap O(depth) path rather than replaying an expensive search.
///
/// The landing is not re-identified here: the recorded identity and geometry are core's to compare against,
/// on the same handle this returns.
///
/// An expired deadline is the one failure that must not settle. There was no attempt left to settle: a path
/// walk cut short by the deadline lands short of the stored index, and reporting that as a miss would tell
/// core the element is genuinely gone when the truth is only that time ran out. It surfaces as TIMEOUT instead.
/// </summary>
internal static class ResolveAnchor
{
    private const string AnchorContext = "walk a locator anchor's path";

    /// <summary>
    /// The anchor's descent policy.
    ///
    /// An expired deadline surfaces the timeout rather than yielding a partial list. This resolver settles in
    /// one attempt, so a partial list would land short of the stored index and report a settled STALE_REF,
    /// telling the caller the element is genuinely gone when the truth is only that time ran out, and consuming
    /// the fresh re-observation the settled answer is supposed to trigger.
    /// </summary>
    internal static readonly DescentPolicy AnchorDescent = new(AnchorDescentVerdict, AnchorContext, AnchorContext);

    /// <summary>
    /// Resolves a locator anchor, settled-on-churn and bounded on transport.
    /// </summary>
    internal static NativeHandle ResolveLocatorAnchor(RefEntry entry, Deadline deadline) =>
        ElementResolve.RetryIncompleteUntil(deadline, () => ResolveLocatorAnchorOnce(entry, deadline));

    /// <summary>
    /// The one pure gate runs first, before any of ResolveWindowRoot, UiaTreeSource.ForRoot or PrepareRoot:
    /// three cross-process calls a verdict the stored ref alone can settle should never pay for.
    ///
    /// That gate is the path's scope: a path recorded relative to a drill-down root is not one the window's own
    /// root can descend, and an empty path is a valid anchor because the root element itself can be the stored
    /// target. Everything about the element itself (name, identifier, bounds) belongs to core's full-predicate
    /// check on the landing, so an element that carries none of them still lands.
    /// </summary>
    private static NativeHandle ResolveLocatorAnchorOnce(RefEntry entry, Deadline deadline)
    {
        if (!ResolveSearch.WindowRooted(entry))
        {
            throw ResolveMatch.BuildStaleRefError(entry);
        }

        var root = ElementResolve.ResolveWindowRoot(entry, deadline);
        var source = UiaTreeSource.ForRoot(root);
        var prepared = source.PrepareRoot(root);
        var budget = ResolveSearch.ResolveWalkBudget(deadline);

        var landing = ResolveDescent.DescendPath(source, prepared, entry.Scope.Path, budget, AnchorDescent);
        if (landing is null)
        {
            throw ResolveMatch.BuildStaleRefError(entry);
        }

        var (_, evidence, _) = source.Evidence(landing);
        AnchorRoleVerdict(entry, evidence);
        return ElementResolve.IntoVerifiedHandle(landing, entry);
    }

    /// <summary>
    /// The anchor's role guard: an unknown role is a failed read, not evidence the element is some other role,
    /// so it stays retryable; anything else is a settled verdict, either a role match (the walk continues) or a
    /// role-refuted landing (the path is a locator, never an identity by itself).
    /// </summary>
    private static void AnchorRoleVerdict(RefEntry entry, LocatorEvidence evidence)
    {
        if (evidence.Role.IsUnknown())
        {
            throw ResolveSearch.IdentityUnknownError(entry);
        }

        var roleMatches = evidence.Role.IsKnown() && evidence.Role.Value is { } role && role == entry.Identity.Role;
        if (!roleMatches)
        {
            throw ResolveMatch.BuildStaleRefError(entry);
        }
    }

    /// <summary>
    /// Anchor descent classification: a settled absence or a vanished node means the subtree enumerates nothing
    /// (a real settled miss, never retried); a transport failure propagates retryable; a terminal failure
    /// propagates as the attempt's error. The stale-produced-by-completion shape (STALE_REF with complete: true,
    /// retryability derived) is what satisfies core's hydration retry predicate so the fresh re-observation fires.
    /// </summary>
    private static DescentVerdict AnchorDescentVerdict(AutomationClassify.Failure failure) =>
        AutomationClassify.DispositionOf(failure) switch
        {
            AutomationClassify.ReadDisposition.SettledAbsence
                or AutomationClassify.ReadDisposition.Unavailable => DescentVerdict.Settled,
            AutomationClassify.ReadDisposition.Retryable
                or AutomationClassify.ReadDisposition.Terminal => DescentVerdict.Surfaced,
            _ => DescentVerdict.Surfaced,
        };
}
