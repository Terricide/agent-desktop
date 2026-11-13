namespace AgentDesktop.Core.Tree;

/// <summary>Text as a query sees it. A search compares text with its case folded, its
/// whitespace runs collapsed to one space, and the bidirectional control characters removed,
/// because a name a runtime publishes carries formatting a caller typing a query will not
/// type. A needle is normalized once and a haystack is normalized on each comparison, so the
/// needle is the normalized form and the haystack is compared against it.</summary>
public static class SearchText
{
    public static string Normalize(string value)
    {
        var normalized = new System.Text.StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var character in value)
        {
            if (IsBidirectionalControl(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = normalized.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }

            normalized.Append(char.ToLowerInvariant(character));
        }

        return normalized.ToString();
    }

    public static bool IsBidirectionalControl(char character) => character switch
    {
        '\u061c' or '\u200e' or '\u200f' or '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e'
            or '\u2066' or '\u2067' or '\u2068' or '\u2069' => true,
        _ => false,
    };

    /// <summary>An empty needle is contained in every haystack, which is what an empty query
    /// means: it rules nothing out.</summary>
    public static bool Contains(string haystack, string normalizedNeedle) =>
        normalizedNeedle.Length == 0 || Normalize(haystack).Contains(normalizedNeedle);
}
