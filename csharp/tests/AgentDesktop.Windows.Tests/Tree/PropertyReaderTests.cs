using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Tree.Properties;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>
/// The reader's contract without a desktop session: a zero element pointer is never called
/// through, so every property it is asked for reads as unread, and the properties whose UI
/// Automation id is not declared fault before the element is reached. The secure-field gate is
/// decided from the batch, so each of its three input states is asserted here.
/// </summary>
public sealed class PropertyReaderTests
{
    private static readonly Element Unreachable = new(IntPtr.Zero);

    /// The properties the walk asks for and the mapping refuses. Each faults, so a batch that
    /// asks for them carries an error naming the property.
    private static readonly TreeProperty[] Undeclared =
    [
        TreeProperty.LegacyValue,
        TreeProperty.IsControlElement,
        TreeProperty.IsContentElement,
        TreeProperty.LegacyAvailable,
    ];

    private static PropertyOutcome Known(PropertyValue value) => new PropertyOutcome.Known(value);

    [Fact]
    public void Every_walk_property_an_unreachable_element_cannot_answer_reads_as_unknown()
    {
        var (properties, _) = PropertyReader.ReadLive(Unreachable);
        foreach (var property in PropertyIds.WalkSet)
        {
            if (property.IsElementValued())
            {
                continue;
            }

            Assert.True(
                properties.Get(property).IsUnknown(),
                $"{property.AsString()} answered a value the element was never called through");
        }
    }

    /// The label relation yields an element, not a value, so it is not part of the batch. It is
    /// never read, which is why it must not read as an answer.
    [Fact]
    public void The_label_relation_is_never_in_the_batch()
    {
        var (properties, _) = PropertyReader.ReadLive(Unreachable);
        Assert.False(properties.Get(TreeProperty.LabeledBy) is PropertyOutcome.Known);
    }

    /// A property with no declared id faults before the element is reached, so the batch reports
    /// the refusal by name rather than silently leaving a slot empty.
    [Fact]
    public void An_undeclared_property_faults_before_the_element_is_reached()
    {
        foreach (var property in Undeclared)
        {
            var error = Assert.Throws<AdapterError>(() => PropertyReader.ReadOne(Unreachable, property));
            Assert.Contains(property.AsString(), error.Message);
        }
    }

    [Fact]
    public void The_batch_names_every_property_it_refused()
    {
        var (_, errors) = PropertyReader.ReadLive(Unreachable);
        Assert.Equal(Undeclared.Length, errors.Count);
        foreach (var property in Undeclared)
        {
            Assert.True(
                errors.Any(error => error.Message.Contains(property.AsString())),
                $"{property.AsString()} was refused and the batch does not say so");
        }
    }

    /// A closed gate withholds: the content comes from the target, so a control reporting
    /// IsPassword publishes a secret into the snapshot, the session segment, and the trace export.
    [Fact]
    public void A_closed_secure_gate_withholds_every_value_bearing_property()
    {
        var reads = new List<(TreeProperty, PropertyOutcome)>
        {
            (TreeProperty.IsPassword, Known(new PropertyValue.Flag(true))),
            (TreeProperty.Name, Known(new PropertyValue.Text("hunter2"))),
            (TreeProperty.Value, Known(new PropertyValue.Text("hunter2"))),
            (TreeProperty.AutomationId, Known(new PropertyValue.Text("submit"))),
        };
        var properties = ElementProperties.FromReads(reads);

        Assert.True(properties.Secure);
        Assert.True(properties.Get(TreeProperty.Name).IsAbsent());
        Assert.True(properties.Get(TreeProperty.Value).IsAbsent());
        Assert.False(properties.Get(TreeProperty.AutomationId).IsAbsent());
    }

    /// An unread gate is not evidence the element is safe, so it withholds. An absent gate is a
    /// real answer: not a password field.
    [Fact]
    public void An_unread_gate_withholds_and_an_absent_gate_does_not()
    {
        var unread = ElementProperties.FromReads(
            [(TreeProperty.IsPassword, PropertyOutcome.Unknown), (TreeProperty.Name, Known(new PropertyValue.Text("Send")))]);
        Assert.True(unread.Secure);
        Assert.True(unread.Get(TreeProperty.Name).IsAbsent());

        var absent = ElementProperties.FromReads(
            [(TreeProperty.IsPassword, PropertyOutcome.Absent), (TreeProperty.Name, Known(new PropertyValue.Text("Send")))]);
        Assert.False(absent.Secure);
        Assert.True(absent.Get(TreeProperty.Name) is PropertyOutcome.Known);
    }

    /// A provider that answers IsPassword as an integer, where UIA documents a bool, is accepted
    /// as true, so the gate cannot be opened by a shape the reader did not expect.
    [Fact]
    public void A_non_zero_integer_gate_closes()
    {
        var properties = ElementProperties.FromReads(
            [(TreeProperty.IsPassword, Known(new PropertyValue.Number(1))), (TreeProperty.Name, Known(new PropertyValue.Text("pw")))]);

        Assert.True(properties.Secure);
        Assert.True(properties.Get(TreeProperty.Name).IsAbsent());
    }

    /// An expired budget stops between properties, so the set is truncated: every slot is left
    /// unread and the stop is reported once, not once per remaining property.
    [Fact]
    public void An_expired_budget_truncates_the_set_and_reports_the_stop_once()
    {
        var (_, errors) = PropertyReader.ReadLiveBounded(Unreachable, Deadline.DetachedAfter(0));

        Assert.Single(errors);
        Assert.Equal(ErrorCode.Timeout, errors[0].Code);
    }

    /// The bounded read leaves every property it did not reach in the read set as unread, so the
    /// walk can report the batch as incomplete rather than as a complete observation.
    [Fact]
    public void A_truncated_batch_still_carries_every_slot_it_was_asked_for()
    {
        var (properties, _) = PropertyReader.ReadLiveBounded(Unreachable, Deadline.DetachedAfter(0));
        foreach (var property in PropertyIds.WalkSet)
        {
            if (property.IsElementValued())
            {
                continue;
            }

            Assert.True(
                properties.Get(property).IsUnknown(),
                $"{property.AsString()} is missing from a truncated batch");
        }
    }

    /// The cache path is the walk's own read. A property the cache request did not name is a
    /// programming error, not a provider that implements nothing, so it must never read as an
    /// answer, and on an unreachable element it reads as unread.
    [Fact]
    public void The_cache_path_never_answers_for_an_element_it_cannot_reach()
    {
        var (properties, _) = PropertyReader.ReadCached(Unreachable);
        foreach (var property in PropertyIds.WalkSet)
        {
            if (property.IsElementValued())
            {
                continue;
            }

            Assert.False(
                properties.Get(property).IsAbsent(),
                $"{property.AsString()} reads as a provider that implements nothing, which is a claim the cache path cannot establish");
            Assert.True(properties.Get(property).IsUnknown());
        }
    }

    /// The bounding rectangle is a dedicated call, not a property read, so the reader must reach
    /// it without asking the mapping for an id the element cannot answer.
    [Fact]
    public void The_bounding_rectangle_of_an_unreachable_element_is_unread()
    {
        Assert.True(PropertyReader.ReadOne(Unreachable, TreeProperty.BoundingRectangle).IsUnknown());
    }
}
