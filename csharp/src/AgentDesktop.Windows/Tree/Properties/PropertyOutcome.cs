using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.Tree.Properties;

/// <summary>One property read, in the three states core's <see cref="LocatorField{T}"/> distinguishes.
/// UI Automation has no per-property error channel. macOS gets a parallel array where an absent slot is
/// `kCFNull` and a failed slot carries its own error; UIA has neither, so this value is built by hand from
/// the not-supported sentinel, the variant tag, and the call's own result.</summary>
internal abstract record PropertyOutcome
{
    /// <summary>Longest string carried into evidence. A value past the bound is Unknown rather than a
    /// truncated Known: a prefix that is presented as exact identity evidence would make re-identification
    /// match the wrong element.</summary>
    public const int MaxEvidenceChars = 2_048;

    private PropertyOutcome()
    {
    }

    public static PropertyOutcome Absent { get; } = new AbsentOutcome();

    public static PropertyOutcome Unknown { get; } = new UnknownOutcome();

    public bool IsAbsent() => this is AbsentOutcome;

    public bool IsUnknown() => this is UnknownOutcome;

    public LocatorField<string> Text() => this switch
    {
        Known { Value: PropertyValue.Text { Value: var text } } => LocatorField<string>.Known(text),
        Known => LocatorField<string>.Unknown(),
        AbsentOutcome => LocatorField<string>.Absent(),
        _ => LocatorField<string>.Unknown(),
    };

    /// <summary>The read's text, or null if it is missing, unreadable, or blank once trimmed. This is the guard
    /// every evidence-building call site needs before trusting a string as identity or descriptive text.</summary>
    public string? NonBlankText()
    {
        var text = Text();
        return text.IsKnown() && !string.IsNullOrWhiteSpace(text.Value) ? text.Value : null;
    }

    public bool? Flag() => this is Known { Value: PropertyValue.Flag flag } ? flag.Value : null;

    public int? Number() => this is Known { Value: PropertyValue.Number number } ? number.Value : null;

    public LocatorField<Rect> Bounds() => this switch
    {
        Known { Value: PropertyValue.Bounds { Value: var rect } } => LocatorField<Rect>.Known(rect),
        Known => LocatorField<Rect>.Unknown(),
        AbsentOutcome => LocatorField<Rect>.Absent(),
        _ => LocatorField<Rect>.Unknown(),
    };

    public sealed record Known(PropertyValue Value) : PropertyOutcome;

    private sealed record AbsentOutcome : PropertyOutcome;

    private sealed record UnknownOutcome : PropertyOutcome;
}
