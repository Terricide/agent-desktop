using System.IO;
using System.Text.RegularExpressions;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Tree.Properties;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>
/// The UI Automation half of the vocabulary pins: which property ids resolve, which fail closed,
/// and which names the structured payloads depend on.
/// </summary>
public sealed partial class PropertyIdsTests
{
    /// A2-5 measured that UIA property ids are build-specific and named this module as the place a
    /// hand-written table would fail silently, so the source must contain no bare property-id
    /// integer.
    [Fact]
    public void No_property_id_integer_appears_in_this_module()
    {
        foreach (var (name, path) in VocabularyFiles)
        {
            var file = Path.Combine(SourceRoot, path);
            Assert.True(File.Exists(file), $"{name} is not at {path}, so its property ids cannot be checked");

            foreach (var line in File.ReadAllLines(file))
            {
                Assert.False(ContainsPropertyIdLiteral(line), $"{name} carries a bare property id: {line}");
            }
        }
    }

    [Fact]
    public void Declared_variants_are_every_variant_the_enum_declares()
    {
        var declared = DeclaredVariants();
        Assert.False(declared.Count == 0, "reading no variants would make every gate in this file vacuous");

        var listed = AllProperties.Select(property => property.ToString()).ToList();
        foreach (var variant in declared)
        {
            Assert.True(listed.Contains(variant), $"{variant} is declared on the enum and is missing from AllProperties");
        }

        Assert.Equal(listed.Count, declared.Count);
    }

    /// <c>AsString</c> is the sole identifier of a failed property in the structured error payload
    /// that reaches a session trace segment and the trace export, so two properties sharing a name
    /// would make those payloads unreadable. Asserting only that each name is non-empty passes on
    /// a collision, which is the failure this pins: a name is a value, not a presence.
    [Fact]
    public void Every_property_carries_a_name_no_other_property_uses()
    {
        var seen = new List<string>();
        foreach (var property in AllProperties)
        {
            var name = property.AsString();
            Assert.False(name.Length == 0, $"{property} names itself with nothing");
            Assert.False(seen.Contains(name), $"{property} reports {name}, which another property already reports");
            seen.Add(name);
        }

        Assert.Equal(seen.Count, AllProperties.Length);
    }

    /// Three names spelled out, because the UI Automation spelling and this project's variant
    /// spelling deliberately differ for the legacy family, and a uniqueness check alone would
    /// accept a wholesale renaming.
    [Fact]
    public void The_names_that_do_not_match_their_variant_spelling_are_pinned()
    {
        Assert.Equal("LegacyIAccessibleValue", TreeProperty.LegacyValue.AsString());
        Assert.Equal("Name", TreeProperty.Name.AsString());
        Assert.Equal("HelpText", TreeProperty.HelpText.AsString());
    }

    /// A2-5 measured that UI Automation property ids are build-specific, so every id comes from the
    /// generated constants. What that mapping must guarantee is that no two properties resolve to
    /// the same constant: a mis-mapped variant makes the walk read one property's value into
    /// another property's slot, which no amount of "the call returned something" observes.
    ///
    /// Properties whose id is not yet declared in the UIA mapping raise NOT_SUPPORTED, and a
    /// property that cannot be read is not a property that can collide, so those are recorded as
    /// unmapped and excluded from the collision sweep.
    [Fact]
    public void No_two_properties_resolve_to_the_same_automation_constant()
    {
        var seen = new List<(int Id, TreeProperty Property)>();
        foreach (var property in AllProperties)
        {
            int resolved;
            try
            {
                resolved = PropertyUia.ToUiaProperty(property);
            }
            catch (AdapterError)
            {
                continue;
            }

            foreach (var (id, other) in seen)
            {
                Assert.False(id == resolved, $"{property} and {other} both resolve to {resolved}");
            }

            seen.Add((resolved, property));
        }

        Assert.Equal(AllProperties.Length - UnmappedProperties.Length, seen.Count);
    }

    /// The properties whose UIA id is not declared in the mapping. Each fails closed as
    /// NOT_SUPPORTED, so the walk never reads them.
    [Fact]
    public void Properties_without_a_declared_uia_id_fail_closed()
    {
        foreach (var property in UnmappedProperties)
        {
            Assert.Throws<AdapterError>(() =>
            {
                PropertyUia.ToUiaProperty(property);
            });
        }
    }

    [Fact]
    public void ToTreeProperty_inverts_ToUiaProperty_for_every_mapped_property()
    {
        foreach (var property in AllProperties)
        {
            if (UnmappedProperties.Contains(property))
            {
                continue;
            }

            Assert.Equal((TreeProperty?)property, PropertyUia.ToTreeProperty(PropertyUia.ToUiaProperty(property)));
        }
    }

    [Fact]
    public void ToTreeProperty_answers_null_for_an_id_no_vocabulary_entry_declares()
    {
        Assert.Null(PropertyUia.ToTreeProperty(AgentDesktop.Windows.Uia.PropertyIds.FrameworkId));
    }

    /// The UIA mapping is exhaustive: every variant resolves, through a declared id or through a
    /// NOT_SUPPORTED refusal, and never through an unhandled value.
    [Fact]
    public void Every_property_resolves_through_the_uia_mapping()
    {
        foreach (var property in AllProperties)
        {
            try
            {
                PropertyUia.ToUiaProperty(property);
            }
            catch (AdapterError)
            {
                continue;
            }
            catch (ArgumentOutOfRangeException)
            {
                Assert.Fail($"{property} has no arm in the UIA mapping");
            }
        }
    }
}
