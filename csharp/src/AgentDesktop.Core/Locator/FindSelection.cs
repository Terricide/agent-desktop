using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Locator;

/// <summary>Which matches a find returns. `--nth` is 0-indexed, `--limit` defaults to 50, and
/// `--limit 0` asks for all of them, so a caller who wants the whole list is not silently
/// truncated.</summary>
public static class FindSelection
{
    public const int DefaultLimit = 50;

    public static List<Match> Pick(FindSelectionArgs selection, List<Match> matches)
    {
        if (selection.First)
        {
            return matches.Take(1).ToList();
        }

        if (selection.Last)
        {
            return matches.TakeLast(1).ToList();
        }

        if (selection.Nth is int nth)
        {
            return nth < matches.Count ? new List<Match> { matches[nth] } : new List<Match>();
        }

        var limit = selection.Limit ?? DefaultLimit;
        return limit == 0 ? matches : matches.Take(limit).ToList();
    }
}

/// <summary>A node a find matched, with the labels of its ancestors so a caller can tell which
/// part of the tree the match sits in.</summary>
public sealed record Match(AccessibilityNode Node, List<string> Path);
