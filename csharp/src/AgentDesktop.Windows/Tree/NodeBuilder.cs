using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>Projects one element's read set into the node core consumes, the way core's
/// observed-tree projection does: an unknown role is "unknown", a field that is unknown or
/// empty is omitted, and unknown states or actions are an empty list.</summary>
internal static class NodeBuilder
{
    internal static AccessibilityNode Build(ElementProperties properties, LabelOutcome label, List<AccessibilityNode> children, uint? childrenCount, bool subtreeTruncated)
    {
        var role = Roles.Resolve(properties);
        var evidence = NameEvidenceFactory.EvidenceOf(properties, label);
        var status = NameEvidenceFactory.StatusOf(properties, label);
        var name = AccName.ResolveName(evidence, status);
        var description = AccName.ResolveDescription(evidence, status);
        var fullDescription = properties.NonBlankText(TreeProperty.FullDescription);
        var helpText = properties.NonBlankText(TreeProperty.HelpText);
        return new AccessibilityNode
        {
            Role = role,
            Identity = new NodeIdentity
            {
                Name = name,
                Value = Value(properties),
                Description = description,
                NativeId = properties.NonBlankText(TreeProperty.AutomationId) is { } automationId
                    ? new ElementIdentifier(IdentifierKind.AutomationId, automationId)
                    : null,
            },
            Presentation = new NodePresentation
            {
                States = NodeStates.Resolve(properties, role) ?? new List<string>(),
                AvailableActions = AvailableActions.Resolve(properties) ?? new List<string>(),
                Bounds = BoundsOf(properties),
                Descriptors = new NodeDescriptor
                {
                    Subrole = properties.NonBlankText(TreeProperty.AriaRole),
                    RoleDescription = properties.NonBlankText(TreeProperty.LocalizedControlType),
                    Placeholder = fullDescription is not null ? helpText : null,
                },
            },
            ChildrenCount = childrenCount,
            SubtreeTruncated = subtreeTruncated,
            Children = children,
        };
    }

    /// <summary>A RichEdit control terminates its value with a carriage return no user typed.</summary>
    private static LocatorField<string> Value(ElementProperties properties)
    {
        var value = properties.Text(TreeProperty.Value);
        if (value is not null
            && value.EndsWith('\r')
            && properties.Text(TreeProperty.ClassName) is { } className
            && className.StartsWith("richedit", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^1];
        }

        return string.IsNullOrEmpty(value) ? LocatorField<string>.Absent() : LocatorField<string>.Known(value);
    }

    private static Rect? BoundsOf(ElementProperties properties)
    {
        var bounds = properties.Get(TreeProperty.BoundingRectangle).Bounds();
        return bounds.IsKnown() && bounds.Value.IsFiniteBounded() ? bounds.Value : null;
    }
}
