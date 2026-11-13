using AgentDesktop.Core.Response;

namespace AgentDesktop.Core.Systems;

/// <summary>Picks the window a command observes from the windows the platform listed for
/// its app. One candidate is the answer. Several are narrowed by visibility, then by a
/// single focused window, then by accessibility; a tie is refused with the candidates, so
/// the caller can retry with an explicit id.</summary>
public static class WindowLookup
{
    /// <summary>The windows that can own a surface, ordered: focused first, then visible,
    /// then the rest, each group in the order the platform listed.</summary>
    public static List<WindowInfo> SurfaceOwnerOrder(List<WindowInfo> candidates)
    {
        return [.. candidates.OrderBy(w => !w.State.IsFocused).ThenBy(w => w.State.Visible != true)];
    }

    /// <summary>The window a scope names, or the app's own window when the scope is empty.</summary>
    public static WindowInfo Select(List<WindowInfo> windows, WindowScope scope, string app)
    {        var candidates = windows.Where(w => w.App == app).ToList();
        if (candidates.Count == 0)
        {
            throw new AdapterError(ErrorCode.WindowNotFound, $"No window found for app '{app}'")
                .WithSuggestion("Run 'list-windows' and retry with --window-id <id>");
        }

        if (scope.WindowId is not null)
        {
            var matches = candidates.Where(w => w.Id == scope.WindowId).ToList();
            if (matches.Count == 0)
            {
                throw new AdapterError(ErrorCode.WindowNotFound, $"Window '{scope.WindowId}' not found")
                    .WithSuggestion("Run 'list-windows' and retry with --window-id <id>");
            }

            return matches[0];
        }

        if (scope.Title is not null)
        {
            var matches = candidates.Where(w => w.Title == scope.Title).ToList();
            if (matches.Count == 0)
            {
                throw new AdapterError(ErrorCode.WindowNotFound, $"No window with title '{scope.Title}'")
                    .WithSuggestion("Run 'list-windows' and retry with --window-id <id>");
            }

            return matches[0];
        }

        if (scope.NearX is not null && scope.NearY is not null)
        {
            return Nearest(candidates, scope.NearX.Value, scope.NearY.Value);
        }

        return SelectOwned(candidates, app);
    }

    private static WindowInfo Nearest(List<WindowInfo> candidates, double x, double y)
    {
        var best = candidates[0];
        var bestDistance = Distance(best, x, y);
        foreach (var candidate in candidates.Skip(1))
        {
            var distance = Distance(candidate, x, y);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static double Distance(WindowInfo window, double x, double y)
    {
        if (window.Bounds is null)
        {
            return double.MaxValue;
        }

        var rect = window.Bounds.Value;
        var cx = rect.X + rect.Width / 2.0;
        var cy = rect.Y + rect.Height / 2.0;
        return (cx - x) * (cx - x) + (cy - y) * (cy - y);
    }

    /// <summary>The window one surface reads, from the windows the platform listed for it. One
    /// candidate is the answer. Several are narrowed by visibility, then by the first focused
    /// window, then by accessibility; a tie is refused with the candidates, so the caller can
    /// retry with an explicit id.</summary>
    public static WindowInfo SelectWindow(List<WindowInfo> candidates, AdapterError empty, string ambiguousMessage)
    {
        if (candidates.Count == 0)
        {
            throw empty.Unsent();
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var list = candidates;
        if (list.Any(w => w.State.Visible == true))
        {
            list = list.Where(w => w.State.Visible == true).ToList();
            if (list.Count == 1)
            {
                return list[0];
            }
        }

        var focused = list.FindIndex(w => w.State.IsFocused);
        if (focused >= 0 && !list.Skip(focused + 1).Any(w => w.State.IsFocused))
        {
            return list[focused];
        }

        if (list.Any(w => w.State.Accessible))
        {
            list = list.Where(w => w.State.Accessible).ToList();
            if (list.Count == 1)
            {
                return list[0];
            }
        }

        throw Ambiguous(list, ambiguousMessage);
    }

    private static WindowInfo SelectOwned(List<WindowInfo> candidates, string app)
    {
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var visible = candidates.Where(w => w.State.Visible == true).ToList();
        if (visible.Count > 0)
        {
            candidates = visible;
            if (candidates.Count == 1)
            {
                return candidates[0];
            }
        }

        var focused = candidates.Where(w => w.State.IsFocused).ToList();
        if (focused.Count == 1)
        {
            return focused[0];
        }

        var accessible = candidates.Where(w => w.State.Accessible).ToList();
        if (accessible.Count == 1)
        {
            return accessible[0];
        }

        throw Ambiguous(candidates, $"Multiple windows match app '{app}'");
    }

    /// <summary>The refusal for a tie: the message says what was matched, and the details carry
    /// the candidates so the caller can pick an id.</summary>
    private static AdapterError Ambiguous(List<WindowInfo> candidates, string message)
    {
        var shown = Math.Min(candidates.Count, 10);
        var summaries = new EnvelopeWriter();
        summaries.ArrayStart();
        for (var index = 0; index < shown; index++)
        {
            var window = candidates[index];
            summaries.Member();
            summaries.ObjectStart();
            summaries.Key("id");
            summaries.String(window.Id);
            summaries.Key("title");
            summaries.String(window.Title);
            summaries.Key("is_focused");
            summaries.Bool(window.State.IsFocused);
            summaries.Key("visible");
            summaries.Bool(window.State.Visible == true);
            summaries.ObjectEnd();
        }

        summaries.ArrayEnd();

        return new AdapterError(
            ErrorCode.AmbiguousTarget, message)
            .WithSuggestion("Run 'list-windows' and retry with --window-id <id>.")
            .WithDetails(new DetailsBuilder()
                .Add("candidate_count", candidates.Count)
                .Add("candidate_summaries_truncated", candidates.Count > shown)
                .AddRaw("candidates", summaries.Text())
                .Text());
    }
}
