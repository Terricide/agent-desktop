namespace AgentDesktop.Core.Locator;

/// <summary>Outcome of strict resolution. Zero live matches is a stale ref, two or more is
/// an ambiguous target, and a ref that was not unique at capture is refused before the
/// application is read.</summary>
public enum ResolutionOutcome
{
    Unique,
    Stale,
    Ambiguous,
    RefusedAtCapture,
}

/// <summary>The result of resolving one ref against one complete observation.</summary>
public sealed record ResolutionResult(ResolutionOutcome Outcome, Refs.RefEntry? Match, int MatchCount, string? Reason);

/// <summary>Resolves refs from capture proof, never from control values or bounds.</summary>
public static class StrictResolver
{
    public static ResolutionResult Resolve(IReadOnlyList<Refs.RefEntry> observed, Refs.RefEntry target)
    {
        if (!target.Identity.IsCaptureUnique)
        {
            return new ResolutionResult(ResolutionOutcome.RefusedAtCapture, null, 0, target.Identity.UniquenessReason);
        }

        if (target.Identity.StableName.IsUnknown())
        {
            return new ResolutionResult(
                ResolutionOutcome.RefusedAtCapture,
                null,
                0,
                "the capture recorded no name because the adapter could not read it, so there is no proof to re-identify against");
        }

        var matches = observed.Where(e => e.Identity.MatchesCapture(target.Identity)).ToList();
        if (matches.Count == 0)
        {
            return new ResolutionResult(ResolutionOutcome.Stale, null, 0, null);
        }
        if (matches.Count > 1)
        {
            return new ResolutionResult(ResolutionOutcome.Ambiguous, null, matches.Count, null);
        }
        return new ResolutionResult(ResolutionOutcome.Unique, matches[0], 1, null);
    }
}
