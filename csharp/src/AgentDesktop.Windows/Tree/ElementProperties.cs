using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>
/// Every property read for one element, keyed by <see cref="TreeProperty"/> and already gated on
/// <c>IsPassword</c>. A property that was never read is unknown. When <c>IsPassword</c> reads true
/// or cannot be read, every value-bearing property is withheld: a known value becomes absent, so a
/// secure field never carries its text. Mirrors ElementProperties in crates/windows/src/tree/properties.
/// </summary>
internal sealed class ElementProperties
{
    private readonly Dictionary<TreeProperty, PropertyOutcome> _reads;

    internal ElementProperties(Dictionary<TreeProperty, PropertyOutcome> reads)
    {
        _reads = reads;
        Secure = reads.TryGetValue(TreeProperty.IsPassword, out var password) && WithholdsContent(password);
        if (!Secure)
        {
            return;
        }

        foreach (var property in PropertyIds.ValueBearing)
        {
            if (_reads.TryGetValue(property, out var outcome) && outcome is PropertyOutcome.Known)
            {
                _reads[property] = PropertyOutcome.Absent;
            }
        }
    }

    /// <summary>Whether the secure-field gate is closed for this element.</summary>
    internal bool Secure { get; }

    /// <summary>
    /// Decides the secure-field gate from the <c>IsPassword</c> outcome and withholds every
    /// value-bearing property when the gate is closed. The first entry for a property is the one
    /// <see cref="Get"/> answers.
    /// </summary>
    /// <remarks>
    /// Fails closed, because the cost is asymmetric: withholding a name that was not secret loses a
    /// little evidence, while publishing one that was puts a password into a snapshot, a session
    /// JSONL segment and a trace HTML export. A non-zero integer is accepted as true from a provider
    /// that returns <c>VT_I4</c> where UIA documents <c>VT_BOOL</c>. An unread <c>IsPassword</c>
    /// withholds, because a failed read is not evidence the element is safe. An absent one is a real
    /// answer: not a password field. An element whose read set never requested <c>IsPassword</c> is
    /// not gated at all.
    /// </remarks>
    internal static ElementProperties FromReads(IReadOnlyList<(TreeProperty Property, PropertyOutcome Outcome)> reads)
    {
        var keyed = new Dictionary<TreeProperty, PropertyOutcome>(reads.Count);
        foreach (var (property, outcome) in reads)
        {
            keyed.TryAdd(property, outcome);
        }

        return new ElementProperties(keyed);
    }

    /// <summary>Reads IsPassword live from the element and returns true when the element is a
    /// password field or when IsPassword cannot be read, failing closed.</summary>
    internal static bool IsLivePassword(Element element) =>
        WithholdsContent(PropertyUia.ReadOne(element, TreeProperty.IsPassword));

    /// <summary>Overwrites one read. Used to carry a window truth into the read set a projection
    /// serves, where the element's own provider reported something the window contradicts.</summary>
    internal void Set(TreeProperty property, PropertyOutcome outcome) => _reads[property] = outcome;

    internal PropertyOutcome Get(TreeProperty property) =>
        _reads.TryGetValue(property, out var outcome) ? outcome : PropertyOutcome.Unknown;

    /// <summary>The read's text, or null when the read is not a text answer. An unread, absent, or
    /// non-text read has no text.</summary>
    internal string? Text(TreeProperty property) => Get(property).Text().ToNullableText();

    /// <summary>The read's text, or null if it is missing, unreadable, or blank once trimmed.</summary>
    internal string? NonBlankText(TreeProperty property) => Get(property).NonBlankText();

    /// <summary>A property read as a field that can be known, absent, or unread. A known read that
    /// carries no text answers the empty string, so the known answer survives even when its content
    /// does not.</summary>
    internal LocatorField<string> TextField(TreeProperty property)
    {
        var outcome = Get(property);
        if (outcome is PropertyOutcome.Known)
        {
            return LocatorField<string>.Known(outcome.Text().ToNullableText() ?? string.Empty);
        }

        return outcome.IsAbsent() ? LocatorField<string>.Absent() : LocatorField<string>.Unknown();
    }

    /// <summary>A BOOL answer only; a number is not a flag here.</summary>
    internal bool? Flag(TreeProperty property) => Get(property).Flag();

    internal int? Number(TreeProperty property) => Get(property).Number();

    /// <summary>A BOOL or a nonzero number, read only when the pattern that defines the
    /// property is available: a state property of an absent pattern carries no meaning.</summary>
    internal bool? GatedBool(TreeProperty property) => GateOpen(property) ? AsBool(Get(property)) : null;

    internal int? GatedNumber(TreeProperty property) => GateOpen(property) ? Number(property) : null;

    internal bool IsTrue(TreeProperty property) => GatedBool(property) == true;

    /// <summary>
    /// Projects the read set onto the evidence slot shape core consumes. Every interpreted slot is
    /// resolved by a vocabulary module from this same read set and threaded in by the caller, so
    /// this stays a projection and takes no decision of its own, notably the name, which core
    /// computes and no adapter does.
    /// </summary>
    /// <remarks>
    /// The value is the one place this projects rather than copies. A RichEdit control ends its text
    /// with a paragraph mark (<c>\r</c>) that is not user content, so an emptied field would read
    /// back as <c>"\r"</c> and every written value would gain one. That single trailing mark is
    /// dropped for RichEdit classes, matched case-insensitively; every other control reports its
    /// value verbatim.
    /// </remarks>
    internal LocatorEvidence ToLocatorEvidence(ResolvedVocabulary vocabulary) => new()
    {
        Role = vocabulary.Role,
        Name = vocabulary.Name,
        Description = vocabulary.Description,
        Value = ValueWithoutRichEditTerminator(),
        Identifiers = BuildIdentifierEvidence(),
        States = vocabulary.States,
        RefEvidence = new LocatorRefEvidence
        {
            WindowBounds = LocatorField<Rect>.Absent(),
            Bounds = Get(TreeProperty.BoundingRectangle).Bounds(),
            AvailableActions = vocabulary.AvailableActions,
            Descriptors = Descriptors(),
        },
    };

    private bool GateOpen(TreeProperty property)
    {
        var gate = property.Gate();
        if (gate is not { } gateProperty)
        {
            return true;
        }

        return AsBool(Get(gateProperty)) == true;
    }

    private LocatorField<string> ValueWithoutRichEditTerminator()
    {
        var text = Get(TreeProperty.Value).Text();
        if (text.IsKnown() && text.Value is { } value && IsRichEdit() && value.EndsWith('\r'))
        {
            return LocatorField<string>.Known(value[..^1]);
        }

        return text;
    }

    private bool IsRichEdit() =>
        Get(TreeProperty.ClassName) is PropertyOutcome.Known { Value: PropertyValue.Text { Value: var className } }
        && className.StartsWith("richedit", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The identifiers the read set reported. A blank automation id is reported as absent, because
    /// an empty string is not identity evidence, and an unread one is refused as evidence rather than
    /// recorded as a confirmed absence.
    /// </summary>
    private IdentifierEvidence BuildIdentifierEvidence()
    {
        var automationId = Get(TreeProperty.AutomationId);
        if (automationId is PropertyOutcome.Known { Value: PropertyValue.Text { Value: var value } } && !string.IsNullOrWhiteSpace(value))
        {
            return new IdentifierEvidence
            {
                Present = true,
                Identifiers = [new ElementIdentifier(IdentifierKind.AutomationId, value)],
            };
        }

        return automationId.IsUnknown()
            ? throw AdapterError.NotSupported("IdentifierEvidence.Unknown")
            : IdentifierEvidence.Absent();
    }

    /// <summary>The descriptor slots. The descriptor vocabulary that fills them has no ported reader,
    /// so every slot is absent. The envelope omits absent slots, so a node carries no descriptor fields
    /// at all rather than fields that claim a value the read set never supplied.</summary>
    private static NodeDescriptor Descriptors() => new NodeDescriptor();

    private static bool? AsBool(PropertyOutcome outcome) =>
        outcome is PropertyOutcome.Known known ? KnownAsBool(known.Value) : null;

    private static bool? KnownAsBool(PropertyValue value) => value switch
    {
        PropertyValue.Flag flag => flag.Value,
        PropertyValue.Number number => number.Value != 0,
        _ => null,
    };

    private static bool WithholdsContent(PropertyOutcome outcome) => outcome switch
    {
        PropertyOutcome.Known known => KnownAsBool(known.Value) ?? true,
        _ when outcome.IsAbsent() => false,
        _ => true,
    };
}
