namespace AgentDesktop.Core.Locator;

/// <summary>Whether a field was observed, reported absent, or left unread. An unread field is
/// refused as incomplete evidence; an absent one reads as empty.</summary>
public enum LocatorFieldKind
{
    Known,
    Absent,
    Unknown,
}

/// <summary>A field that can be known, absent, or unread. `null` cannot carry that
/// distinction, so a field the adapter never read is recorded as Unknown, not as absent.</summary>
public sealed record LocatorField<T>
{
    public LocatorFieldKind Kind { get; init; }
    public T? Value { get; init; }

    public static LocatorField<T> Known(T value) => new() { Kind = LocatorFieldKind.Known, Value = value };

    public static LocatorField<T> Absent() => new() { Kind = LocatorFieldKind.Absent };

    public static LocatorField<T> Unknown() => new() { Kind = LocatorFieldKind.Unknown };

    public bool IsKnown() => Kind == LocatorFieldKind.Known;

    public bool IsAbsent() => Kind == LocatorFieldKind.Absent;

    public bool IsUnknown() => Kind == LocatorFieldKind.Unknown;
}

/// <summary>Where a field is known, absent, or unread, the adapter's read is a field, not a
/// bare string. A read that came back empty means the element has nothing; a read that never
/// happened is unknown and is refused as evidence.</summary>
public static class LocatorFieldExtensions
{
    public static LocatorField<string> NonBlankToField(this string? value) =>
        value is null
            ? LocatorField<string>.Unknown()
            : string.IsNullOrWhiteSpace(value) ? LocatorField<string>.Absent() : LocatorField<string>.Known(value);

    /// <summary>An empty string is not text a caller reads, so a field read as empty is the
    /// same as one reported absent. A field that was never read stays unknown.</summary>
    public static LocatorField<string> NonBlankField(this LocatorField<string> field) =>
        field.IsUnknown() ? LocatorField<string>.Unknown() : NonBlankToField(field.Value);

    public static string? ToNullableText(this LocatorField<string> field) =>
        field.IsKnown() ? field.Value : null;
}
