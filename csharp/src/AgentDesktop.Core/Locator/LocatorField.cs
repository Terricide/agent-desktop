namespace AgentDesktop.Core.Locator;

/// <summary>Whether a field was observed, reported absent, or left unread. An unread field is refused as incomplete evidence; an absent one reads as empty.</summary>
public enum LocatorFieldKind
{
    Known,
    Absent,
    Unknown,
}

public sealed record LocatorField<T>
{
    public LocatorFieldKind Kind { get; init; }
    public T? Value { get; init; }

    public static LocatorField<T> Known(T value) => new() { Kind = LocatorFieldKind.Known, Value = value };

    public static LocatorField<T> Absent() => new() { Kind = LocatorFieldKind.Absent };

    public static LocatorField<T> Unknown() => new() { Kind = LocatorFieldKind.Unknown };

    public bool IsUnknown() => Kind == LocatorFieldKind.Unknown;
}
