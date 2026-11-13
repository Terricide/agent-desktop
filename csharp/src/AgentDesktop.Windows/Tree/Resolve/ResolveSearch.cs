using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree.Resolve;

/// <summary>The stored-path landing family: the scope gate the locator anchor applies and the
/// bounded child enumeration it descends through. Identity is not judged here: the landing is
/// re-checked by core's full predicate, which owns identity for every platform.</summary>
internal static class ResolveSearch
{
    /// <summary>The resolve-scoped depth cap. Independently bounded from the walk's own ceiling,
    /// and at least as deep as core's whole-window observation, so every node a capture can count
    /// is one the anchor can land. Electron elements commonly sit at depth 25+, so the cap is the
    /// resolution bound rather than the walk bound.</summary>
    internal const byte MaxResolveDepth = 50;

    /// <summary>The budget a resolution attempt enumerates under. The sibling bound is the
    /// observation walk's own <see cref="WalkBudget.DefaultMaxSiblings"/> rather than a
    /// resolve-scoped number, and that is load-bearing. A cap-hit reports a whole child list, so a
    /// truncation here settles STALE_REF instead of retrying; that verdict is only honest while the
    /// resolver reaches at least as far as the walk that issued the ref. A lower bound here would
    /// convert reachable refs into settled misses with nothing else refuting them.</summary>
    internal static WalkBudget ResolveWalkBudget(Deadline deadline) =>
        new WalkBudget(MaxResolveDepth, deadline).WithMaxRawDepth(MaxResolveDepth);

    /// <summary>Whether the stored path is scoped to the window root rather than to a drill-down
    /// ancestor: no root ref at all, or an absolute path that overrides one. The anchor descends
    /// from the window's own root, so a path relative to some other node is not one it can walk.</summary>
    internal static bool WindowRooted(RefEntry entry) => entry.Scope.RootRef is null || entry.Scope.PathIsAbsolute;

    /// <summary>The incomplete-and-retryable answer for a candidate whose identity could not be read.</summary>
    internal static AdapterError IdentityUnknownError(RefEntry entry) => ResolveErrors.IdentityUnknownError(entry);
}
