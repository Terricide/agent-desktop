using System.IO;
using System.Text.RegularExpressions;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Tree.Properties;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>
/// Pins the walk's property vocabulary: which properties the walk reads, which gates fire, which
/// names reach the structured error payload, and that no UI Automation property id is written by
/// hand into the vocabulary.
/// </summary>
public sealed partial class PropertyIdsTests
{
    private static readonly string SourceRoot = FindSourceRoot();

    /// Every variant of <c>TreeProperty</c>, listed once for the identity gates below. Completeness
    /// is not trusted to a reader: <c>Declared_variants_are_every_variant_the_enum_declares</c>
    /// diffs this list against the enum's own declaration read out of the source file, so a variant
    /// added without joining this list fails there rather than quietly escaping every assertion
    /// here.
    private static readonly TreeProperty[] AllProperties =
    [
        TreeProperty.Name,
        TreeProperty.AutomationId,
        TreeProperty.ClassName,
        TreeProperty.HelpText,
        TreeProperty.FullDescription,
        TreeProperty.LabeledBy,
        TreeProperty.Value,
        TreeProperty.LegacyValue,
        TreeProperty.BoundingRectangle,
        TreeProperty.IsPassword,
        TreeProperty.IsOffscreen,
        TreeProperty.IsEnabled,
        TreeProperty.IsControlElement,
        TreeProperty.IsContentElement,
        TreeProperty.IsKeyboardFocusable,
        TreeProperty.HasKeyboardFocus,
        TreeProperty.IsRequiredForForm,
        TreeProperty.IsDataValidForForm,
        TreeProperty.IsDialog,
        TreeProperty.ToggleState,
        TreeProperty.ExpandCollapseState,
        TreeProperty.SelectionItemIsSelected,
        TreeProperty.ValueIsReadOnly,
        TreeProperty.RangeValueIsReadOnly,
        TreeProperty.SelectionCanSelectMultiple,
        TreeProperty.WindowIsModal,
        TreeProperty.LegacyState,
        TreeProperty.LegacyDefaultAction,
        TreeProperty.InvokeAvailable,
        TreeProperty.ToggleAvailable,
        TreeProperty.ExpandCollapseAvailable,
        TreeProperty.SelectionItemAvailable,
        TreeProperty.SelectionAvailable,
        TreeProperty.ValueAvailable,
        TreeProperty.RangeValueAvailable,
        TreeProperty.ScrollAvailable,
        TreeProperty.ScrollItemAvailable,
        TreeProperty.WindowAvailable,
        TreeProperty.GridItemAvailable,
        TreeProperty.TableItemAvailable,
        TreeProperty.LegacyAvailable,
        TreeProperty.ProviderDescription,
        TreeProperty.ControlType,
        TreeProperty.RuntimeId,
        TreeProperty.LocalizedControlType,
        TreeProperty.AriaRole,
    ];

    /// The properties whose UIA id is not declared in the mapping. Each fails closed as
    /// NOT_SUPPORTED, so the walk never reads them.
    private static readonly TreeProperty[] UnmappedProperties =
    [
        TreeProperty.LegacyValue,
        TreeProperty.IsControlElement,
        TreeProperty.IsContentElement,
        TreeProperty.IsDataValidForForm,
        TreeProperty.LegacyAvailable,
        TreeProperty.RuntimeId,
    ];

    /// The files the walk reads properties from, named the way the Rust module names them.
    private static readonly (string Name, string Path)[] VocabularyFiles =
    [
        ("property_ids.rs", "src/AgentDesktop.Windows/Tree/Properties/PropertyIds.cs"),
        ("properties.rs", "src/AgentDesktop.Windows/Tree/Properties/TreeProperty.cs"),
        ("element_properties.rs", "src/AgentDesktop.Windows/Tree/ElementProperties.cs"),
        ("roles.rs", "src/AgentDesktop.Windows/Tree/Roles.cs"),
        ("property_outcome.rs", "src/AgentDesktop.Windows/Tree/Properties/PropertyOutcome.cs"),
        ("name_evidence.rs", "src/AgentDesktop.Windows/Tree/NameEvidenceFactory.cs"),
        ("walk.rs", "src/AgentDesktop.Windows/Tree/Walk.cs"),
        ("node_states.rs", "src/AgentDesktop.Windows/Tree/NodeStates.cs"),
        ("element.rs", "src/AgentDesktop.Windows/Tree/Element.cs"),
        ("automation.rs", "src/AgentDesktop.Windows/Tree/Automation.cs"),
    ];

    /// The variant names the enum declares, read from the source rather than from a second
    /// hand-written list.
    private static List<string> DeclaredVariants()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot, "src/AgentDesktop.Windows/Tree/Properties/TreeProperty.cs"));
        var start = source.IndexOf("enum TreeProperty", StringComparison.Ordinal);
        Assert.True(start >= 0, "the enum declaration is in this file");
        var body = source[start..];
        var end = body.IndexOf("\n}", StringComparison.Ordinal);
        Assert.True(end >= 0, "the enum declaration is closed");

        var variants = new List<string>();
        foreach (var line in body[..end].Split('\n').Skip(1))
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith(','))
            {
                variants.Add(trimmed.TrimEnd(','));
            }
        }

        return variants;
    }

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "csharp", "src", "AgentDesktop.Windows");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "csharp");
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("the csharp source root is not reachable from the test assembly");
    }

    /// Reports whether a line contains a bare integer in UI Automation's property-id range. A token
    /// is a candidate only when it is a whole number, five digits long, and between 30000 and
    /// 30999 — the block UIA allocates property ids from. A prose mention of 300 milliseconds, or a
    /// 30000 ms constant, is not a property id and must not fail this.
    private static bool ContainsPropertyIdLiteral(string line)
    {
        foreach (Match match in Regex.Matches(line, @"[0-9][0-9_]*"))
        {
            var digits = match.Value.Replace("_", "");
            if (digits.Length == 5 && int.TryParse(digits, out var value) && value >= 30000 && value <= 30999)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public void Every_value_bearing_property_is_in_the_walk_set()
    {
        foreach (var property in PropertyIds.ValueBearing)
        {
            Assert.True(
                PropertyIds.WalkSet.Contains(property),
                $"{property.AsString()} is gated but never read, so the gate would never fire");
        }
    }

    /// A15-7 measured every pattern-state property returning a default-looking value on an element
    /// lacking the pattern, so a gate that is not itself in the batch is a gate that cannot fire.
    [Fact]
    public void Every_gate_is_read_in_the_same_batch_as_the_property_it_gates()
    {
        foreach (var property in PropertyIds.WalkSet)
        {
            var gate = property.Gate();
            if (gate is null)
            {
                continue;
            }

            Assert.True(
                PropertyIds.WalkSet.Contains(gate.Value),
                $"{property.AsString()} is gated on {gate.Value.AsString()}, which the walk never reads");
        }
    }

    /// The gate exists because of a measurement, so the set it covers is asserted rather than left
    /// to whoever adds the next state arm.
    [Fact]
    public void Every_pattern_state_property_carries_a_gate()
    {
        foreach (var property in new[]
        {
            TreeProperty.ToggleState,
            TreeProperty.ExpandCollapseState,
            TreeProperty.SelectionItemIsSelected,
            TreeProperty.ValueIsReadOnly,
            TreeProperty.SelectionCanSelectMultiple,
            TreeProperty.WindowIsModal,
        })
        {
            Assert.True(
                property.Gate().HasValue,
                $"{property.AsString()} would be read ungated, which A15-7 measured as a false state on every inert node");
        }

        Assert.False(TreeProperty.Name.Gate().HasValue);
        Assert.False(TreeProperty.IsEnabled.Gate().HasValue);
    }

    /// An element-valued property cannot travel through the tri-state classifier, which decodes
    /// scalars and would report a successful read as a failed one.
    [Fact]
    public void The_only_element_valued_property_is_the_label_relation()
    {
        Assert.True(TreeProperty.LabeledBy.IsElementValued());
        foreach (var property in PropertyIds.WalkSet)
        {
            Assert.Equal(
                property.IsElementValued(),
                property == TreeProperty.LabeledBy);
        }
    }

    /// The gate is only sound if IsPassword arrives in the same batch as the properties it gates;
    /// a separate read would cost a round trip the batched design avoids and would open a window
    /// where the gate has no input.
    [Fact]
    public void The_walk_set_carries_the_flag_that_gates_it()
    {
        Assert.Contains(TreeProperty.IsPassword, PropertyIds.WalkSet);
    }

    [Fact]
    public void The_gate_covers_exactly_the_properties_whose_content_comes_from_the_target()
    {
        Assert.True(TreeProperty.Name.IsValueBearing());
        Assert.True(TreeProperty.Value.IsValueBearing());
        Assert.True(TreeProperty.HelpText.IsValueBearing());
        Assert.True(TreeProperty.LegacyValue.IsValueBearing());
        Assert.True(TreeProperty.FullDescription.IsValueBearing());
        Assert.True(TreeProperty.LegacyDefaultAction.IsValueBearing());

        Assert.False(TreeProperty.AutomationId.IsValueBearing());
        Assert.False(TreeProperty.ClassName.IsValueBearing());
        Assert.False(TreeProperty.BoundingRectangle.IsValueBearing());
        Assert.False(TreeProperty.IsPassword.IsValueBearing());
    }

    /// <c>CarriesTargetText</c> and <c>IsValueBearing</c> are two independent declarations of the
    /// same fact — one on the property itself, one in <c>ValueBearing</c> — and a property that
    /// carries target text must be value-bearing regardless of whether the walk currently reads
    /// it, so this checks the whole enum rather than only the walk set. The sweep runs over
    /// <c>AllProperties</c>, whose completeness
    /// <c>Declared_variants_are_every_variant_the_enum_declares</c> proves against the enum's own
    /// declaration. A hand-written list here would silently narrow to a subset the first time a
    /// variant was added.
    [Fact]
    public void Carries_target_text_implies_value_bearing_across_every_property()
    {
        Assert.True(
            AllProperties.Length > PropertyIds.WalkSet.Count,
            "the sweep must reach past the walk set, or it is not the whole enum");
        foreach (var property in AllProperties)
        {
            Assert.True(
                !property.CarriesTargetText() || property.IsValueBearing(),
                $"{property.AsString()} carries target text by its own declaration but is missing from ValueBearing");
        }
    }

    [Fact]
    public void The_walk_set_has_no_duplicate_entries()
    {
        var seen = new List<TreeProperty>();
        foreach (var property in PropertyIds.WalkSet)
        {
            Assert.False(seen.Contains(property), $"{property.AsString()} appears twice");
            seen.Add(property);
        }
    }

}
