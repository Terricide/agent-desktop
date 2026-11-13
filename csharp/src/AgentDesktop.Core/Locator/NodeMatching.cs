using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Locator;

/// <summary>Whether a node satisfies a query. Each predicate the query names is checked in
/// order, and the first that fails rules the node out. A predicate the query does not name
/// rules nothing out, so an empty query matches every node.</summary>
public static class NodeMatching
{
    public static bool Matches(LocatorQuery query, NodeMatchContext context)
    {
        if (!RoleMatches(query, context.Role))
        {
            return false;
        }

        if (!TextFieldMatches(query.Identity.Name, context.Name, query.Exact))
        {
            return false;
        }

        if (!TextFieldMatches(query.Identity.Description, context.Description, query.Exact))
        {
            return false;
        }

        if (!NativeIdMatches(query.Identity.NativeId, context.NativeId))
        {
            return false;
        }

        if (!TextFieldMatches(query.Identity.Value, context.Value, query.Exact))
        {
            return false;
        }

        if (!HasTextMatches(query.HasText, context, query.Exact))
        {
            return false;
        }

        return query.States.All(predicate => predicate.Matches(context.States));
    }

    public static bool Matches(LocatorQuery query, AccessibilityNode node) =>
        Matches(query, NodeMatchContext.From(node));

    private static bool RoleMatches(LocatorQuery query, string role) =>
        query.Identity.Role is not { } expected
        || RoleQuery.Normalize(expected) == role;

    private static bool TextFieldMatches(string? expected, string? actual, bool exact)
    {
        if (expected is null)
        {
            return true;
        }

        if (actual is null)
        {
            return false;
        }

        return TextMatches(actual, expected, exact);
    }

    private static bool NativeIdMatches(string? expected, string? actual) =>
        expected is null || actual == expected;

    private static bool HasTextMatches(string? expected, NodeMatchContext context, bool exact)
    {
        if (expected is null)
        {
            return true;
        }

        foreach (var text in new[] { context.Name, context.Description, context.Value })
        {
            if (text is not null && TextMatches(text, expected, exact))
            {
                return true;
            }
        }

        return context.Children.Any(child => HasTextMatches(expected, NodeMatchContext.From(child), exact));
    }

    private static bool TextMatches(string actual, string expected, bool exact) =>
        exact
            ? SearchText.Normalize(actual) == expected
            : SearchText.Contains(actual, expected);
}
