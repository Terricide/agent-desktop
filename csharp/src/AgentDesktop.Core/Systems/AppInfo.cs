namespace AgentDesktop.Core.Systems;

/// <summary>A running application the platform identified.</summary>
public sealed record AppInfo
{
    public string Name { get; init; } = "";
    public ProcessId Pid { get; init; }
    public string? BundleId { get; init; }
    public string? ProcessInstance { get; init; }
    public AppPresentation? Presentation { get; init; }

    /// <summary>Whether an id identifies this app, by exact case-insensitive name or bundle identifier. The one predicate every launch-target match uses.</summary>
    public bool MatchesIdentifier(string id) =>
        AppNameMatches(Name, id)
        || (BundleId is not null && BundleId.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static bool AppNameMatches(string actual, string expected)
    {
        if (MatchesAfterFiltering(actual, expected))
        {
            return true;
        }

        if (expected.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesAfterFiltering(actual, expected + ".exe");
    }

    private static bool MatchesAfterFiltering(string actual, string expected) =>
        Filter(actual).Equals(Filter(expected), StringComparison.OrdinalIgnoreCase);

    private static string Filter(string value)
    {
        var kept = new List<char>();
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                kept.Add(char.ToLowerInvariant(c));
            }
        }

        return string.Concat(kept);
    }
}
