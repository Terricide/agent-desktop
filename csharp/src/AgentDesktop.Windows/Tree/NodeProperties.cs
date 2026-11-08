using AgentDesktop.Core.Locator;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>One element's read set, keyed by UI Automation property id. A property that was
/// never read is unknown. When IsPassword reads true or cannot be read, every value-bearing
/// property is withheld: a known value becomes absent, so a secure field never carries its
/// text. Mirrors ElementProperties in crates/windows/src/tree/properties.</summary>
internal sealed class NodeProperties
{
    internal static readonly LocatorField<object> UnknownRead = LocatorField<object>.Unknown();

    internal static readonly LocatorField<object> AbsentRead = LocatorField<object>.Absent();

    private static readonly int[] ValueBearing = { PropertyIds.Name, PropertyIds.ValueValue, PropertyIds.LegacyDefaultAction, PropertyIds.HelpText, PropertyIds.FullDescription };

    private readonly Dictionary<int, LocatorField<object>> _reads;

    internal NodeProperties(Dictionary<int, LocatorField<object>> reads)
    {
        _reads = reads;
        Secure = reads.TryGetValue(PropertyIds.IsPassword, out var password) && WithholdsContent(password);
        if (!Secure)
        {
            return;
        }

        foreach (var property in ValueBearing)
        {
            if (_reads.TryGetValue(property, out var read) && read.Kind == LocatorFieldKind.Known)
            {
                _reads[property] = AbsentRead;
            }
        }
    }

    internal bool Secure { get; }

    internal static LocatorField<object> From<T>(LocatorField<T> field) where T : notnull => field.Kind switch
    {
        LocatorFieldKind.Known => LocatorField<object>.Known(field.Value!),
        LocatorFieldKind.Absent => AbsentRead,
        _ => UnknownRead,
    };

    internal LocatorField<object> Get(int property) =>
        _reads.TryGetValue(property, out var read) ? read : UnknownRead;

    /// <summary>A BOOL answer only; a number is not a flag here.</summary>
    internal bool? Flag(int property) => Get(property).Value is bool flag ? flag : null;

    internal int? Number(int property) => Get(property).Value is int number ? number : null;

    internal string? Text(int property) => Get(property).Value as string;

    internal string? NonBlankText(int property) =>
        Text(property) is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    /// <summary>A BOOL or a nonzero number, read only when the pattern that defines the
    /// property is available: a state property of an absent pattern carries no meaning.</summary>
    internal bool? GatedBool(int property) => GateOpen(property) ? AsBool(Get(property)) : null;

    internal int? GatedNumber(int property) => GateOpen(property) ? Number(property) : null;

    internal bool IsTrue(int property) => GatedBool(property) == true;

    private bool GateOpen(int property) => GateOf(property) is not { } gate || AsBool(Get(gate)) == true;

    private static int? GateOf(int property) => property switch
    {
        PropertyIds.ToggleState => PropertyIds.IsTogglePatternAvailable,
        PropertyIds.ExpandCollapseState => PropertyIds.IsExpandCollapsePatternAvailable,
        PropertyIds.SelectionItemIsSelected => PropertyIds.IsSelectionItemPatternAvailable,
        PropertyIds.ValueIsReadOnly => PropertyIds.IsValuePatternAvailable,
        PropertyIds.RangeValueIsReadOnly => PropertyIds.IsRangeValuePatternAvailable,
        PropertyIds.SelectionCanSelectMultiple => PropertyIds.IsSelectionPatternAvailable,
        PropertyIds.WindowIsModal => PropertyIds.IsWindowPatternAvailable,
        _ => null,
    };

    private static bool? AsBool(LocatorField<object> read) => read.Value switch
    {
        bool flag => flag,
        int number => number != 0,
        _ => null,
    };

    private static bool WithholdsContent(LocatorField<object> read) => read.Kind switch
    {
        LocatorFieldKind.Known => AsBool(read) ?? true,
        LocatorFieldKind.Unknown => true,
        _ => false,
    };
}
