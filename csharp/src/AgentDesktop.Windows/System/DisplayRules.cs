using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows;

/// <summary>Display selection, identity, and ordering rules. These are pure, so they run on every host.</summary>
internal static class DisplayRules
{
    /// <summary>Classifies one enumeration pass. The info read runs first, so it is checked first; the callback stops at the first failing read.</summary>
    internal static EnumerationOutcome ClassifyEnumeration(bool enumerated, bool infoReadFailed, bool dpiReadFailed)
    {
        if (infoReadFailed)
        {
            return EnumerationOutcome.MonitorInfoUnreadable;
        }

        if (dpiReadFailed)
        {
            return EnumerationOutcome.DpiUnreadable;
        }

        return enumerated ? EnumerationOutcome.Completed : EnumerationOutcome.EnumerationFailed;
    }

    /// <summary>A failed read carries leftover data in dpiX, so only a successful call with a positive DPI is evidence of scale.</summary>
    internal static double? EffectiveDpiScale(int effective, uint dpiX) =>
        effective == 0 && dpiX > 0 ? dpiX / 96.0 : null;

    internal static double IntersectionArea(Rect left, Rect right)
    {
        var width = Math.Min(left.X + left.Width, right.X + right.Width) - Math.Max(left.X, right.X);
        var height = Math.Min(left.Y + left.Height, right.Y + right.Height) - Math.Max(left.Y, right.Y);
        return Math.Max(width, 0.0) * Math.Max(height, 0.0);
    }

    /// <summary>Picks the display with the largest overlap with the bounds, then the primary, then the first.</summary>
    internal static DisplayInfo? SelectDisplay(IReadOnlyList<DisplayInfo> displays, Rect? bounds)
    {
        if (bounds is { } target)
        {
            DisplayInfo? best = null;
            var bestArea = double.NegativeInfinity;
            foreach (var display in displays)
            {
                var area = IntersectionArea(target, display.Bounds);
                if (area >= bestArea)
                {
                    best = display;
                    bestArea = area;
                }
            }

            if (best is not null && IntersectionArea(target, best.Bounds) > 0.0)
            {
                return best;
            }
        }

        return displays.FirstOrDefault(display => display.IsPrimary) ?? displays.FirstOrDefault();
    }

    internal static bool DisplayIdentityMatches(DisplayInfo expected, DisplayInfo current) =>
        expected.Id == current.Id
        && expected.Bounds == current.Bounds
        && expected.IsPrimary == current.IsPrimary
        && expected.Scale == current.Scale;

    /// <summary>Orders the primary display first, then by id, mirroring the Rust ordering.</summary>
    internal static List<DisplayInfo> PrimariesFirst(IEnumerable<DisplayInfo> displays) =>
        displays
            .OrderBy(display => display.IsPrimary ? 0 : 1)
            .ThenBy(display => display.Id, StringComparer.Ordinal)
            .ToList();
}
