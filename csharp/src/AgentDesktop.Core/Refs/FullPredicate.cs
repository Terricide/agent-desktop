using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Refs;

/// <summary>
/// The identity fields capture and resolution both compare: role plus whichever of native
/// identifier, stable name and description a node records. Control values and bounds are never
/// identity. Port of <c>FullPredicate</c> in
/// <c>crates/core/src/refs/ref_full_predicate.rs</c>.
/// </summary>
public sealed record FullPredicate(
    string Role,
    string? NativeId,
    string? Name,
    string? Description)
{
    public const int NativeIdBit = 0b001;
    public const int NameBit = 0b010;
    public const int DescriptionBit = 0b100;
    public const int RoleBit = DescriptionBit << 1;

    /// <summary>The predicate a node's own recorded fields form. A blank field is not recorded,
    /// and a field the adapter never read is not recorded, so a node read through a blank name
    /// anchors nothing.</summary>
    public static FullPredicate OfNode(AccessibilityNode node) =>
        new(
            node.Role,
            NonBlank(node.Identity.NativeId?.Value),
            StableName(node.Role, KnownText(node.Identity.Name), KnownText(node.Identity.Value)),
            NonBlank(KnownText(node.Identity.Description)));

    /// <summary>The fields this predicate records, which is the field subset a capture proof
    /// counts it against. An entry that records nothing is not a candidate.</summary>
    public int Mask() =>
        (NativeId is not null ? NativeIdBit : 0)
        | (Name is not null ? NameBit : 0)
        | (Description is not null ? DescriptionBit : 0);

    /// <summary>The one rule capture and resolution apply: every field the predicate records must
    /// equal the live node's, and a field the live read could not answer is unknown, not a
    /// match.</summary>
    public IdentityMatch Compare(AccessibilityNode node)
    {
        var roleMatch = string.IsNullOrWhiteSpace(node.Role)
            ? IdentityMatch.Unknown
            : Role.Trim().ToLowerInvariant() == node.Role.Trim().ToLowerInvariant()
                ? IdentityMatch.Match
                : IdentityMatch.NoMatch;

        var nativeMatch = NativeId is null
            ? IdentityMatch.Match
            : NonBlank(node.Identity.NativeId?.Value) is { } live && live == NativeId
                ? IdentityMatch.Match
                : IdentityMatch.NoMatch;

        var nameMatch = FieldMatch(Name, StableNameField(node.Role, node.Identity.Name, node.Identity.Value));
        var descriptionMatch = FieldMatch(Description, MeaningfulField(node.Identity.Description));

        return Conjunction(Conjunction(roleMatch, nativeMatch), Conjunction(nameMatch, descriptionMatch));
    }

    /// <summary>The stable name of a node: its recorded name, dropped when the role carries a
    /// mutable value and the value repeats that name, because that is the same read twice. A
    /// canonical role keeps its value as identity, because a canonical name is stable content.</summary>
    public static string? StableName(string role, string? name, string? value)
    {
        var recorded = NonBlank(name);
        if (recorded is null)
        {
            return null;
        }

        if (CanonicalRoles.IsMutableValueRole(role) && ValueMatchesName(NonBlank(value), recorded))
        {
            return null;
        }

        return recorded;
    }

    /// <summary>The stable name as the live read answers it: known, absent, or unread. A mutable
    /// value that repeats the name makes the name absent, not a second field.</summary>
    public static LocatorField<string> StableNameField(
        string role,
        LocatorField<string> name,
        LocatorField<string> value)
    {
        var recorded = MeaningfulField(name);
        if (!CanonicalRoles.IsMutableValueRole(role))
        {
            return recorded;
        }

        if (recorded.IsUnknown())
        {
            return recorded;
        }

        return recorded.IsKnown()
            && MeaningfulField(value).IsKnown()
            && ValueMatchesName(MeaningfulField(value).Value, recorded.Value)
                ? LocatorField<string>.Absent()
                : recorded;
    }

    /// <summary>The stable value of a node, which a mutable role never carries.</summary>
    public static string? StableValue(string role, string? value) =>
        CanonicalRoles.IsMutableValueRole(role) ? null : NonBlank(value);

    /// <summary>Whether a capture recorded stable text, which is the identity a node carries
    /// besides an element token or a native identifier.</summary>
    public static bool HasStableTextIdentity(string role, string? name, string? value) =>
        StableName(role, name, value) is not null || StableValue(role, value) is not null;

    /// <summary>Whether a capture recorded identity, so the node is a boundary resolution can
    /// reach a subtree through.</summary>
    public static bool HasMeaningfulIdentity(
        string role,
        string? name,
        string? value,
        string? nativeId,
        string? token) =>
        ElementToken.IsElementToken(token)
        || NonBlank(nativeId) is not null
        || HasStableTextIdentity(role, name, value);

    private static bool ValueMatchesName(string? value, string? name)
    {
        if (value == name)
        {
            return true;
        }

        double? left = ParseNumber(value);
        double? right = ParseNumber(name);
        return left is { } l && right is { } r && l == r;
    }

    private static double? ParseNumber(string? text) =>
        double.TryParse(text, out var number) && double.IsFinite(number) ? number : null;

    private static IdentityMatch FieldMatch(string? expected, LocatorField<string> actual)
    {
        if (expected is null)
        {
            return IdentityMatch.Match;
        }

        if (actual.IsUnknown())
        {
            return IdentityMatch.Unknown;
        }

        return actual.IsKnown() && expected == actual.Value
            ? IdentityMatch.Match
            : IdentityMatch.NoMatch;
    }

    private static IdentityMatch Conjunction(IdentityMatch left, IdentityMatch right)
    {
        if (left == IdentityMatch.NoMatch || right == IdentityMatch.NoMatch)
        {
            return IdentityMatch.NoMatch;
        }

        if (left == IdentityMatch.Unknown || right == IdentityMatch.Unknown)
        {
            return IdentityMatch.Unknown;
        }

        return IdentityMatch.Match;
    }

    /// <summary>A field that came back blank is not recorded, and a field the adapter never read
    /// is not recorded.</summary>
    private static LocatorField<string> MeaningfulField(LocatorField<string> field) =>
        field.IsUnknown()
            ? LocatorField<string>.Unknown()
            : NonBlank(field.Value) is { } text
                ? LocatorField<string>.Known(text)
                : LocatorField<string>.Absent();

    private static string? KnownText(LocatorField<string> field) =>
        field.IsKnown() ? field.Value : null;

    private static string? NonBlank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text;
}

/// <summary>How a recorded field compares against a live read: equal, unequal, or unanswerable.</summary>
public enum IdentityMatch
{
    Match,
    NoMatch,
    Unknown,
}
