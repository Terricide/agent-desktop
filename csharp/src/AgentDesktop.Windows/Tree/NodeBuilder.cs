using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>Projects one element's read set into the node core consumes, the way core's
/// observed-tree projection does: an unknown role is "unknown", a field that is unknown or
/// empty is omitted, and unknown states or actions are an empty list.</summary>
internal static class NodeBuilder
{
    internal static AccessibilityNode Build(NodeProperties properties, List<AccessibilityNode> children, uint? childrenCount, bool subtreeTruncated)
    {
        var role = Roles.Resolve(properties);
        return new AccessibilityNode
        {
            Role = role,
            Identity = new NodeIdentity
            {
                Name = properties.NonBlankText(PropertyIds.Name),
                Value = Value(properties),
                NativeId = properties.NonBlankText(PropertyIds.AutomationId) is { } automationId
                    ? new ElementIdentifier(IdentifierKind.AutomationId, automationId)
                    : null,
            },
            Presentation = new NodePresentation
            {
                States = NodeStates.Resolve(properties, role) ?? new List<string>(),
                AvailableActions = AvailableActions.Resolve(properties) ?? new List<string>(),
                Bounds = properties.Get(PropertyIds.BoundingRectangle).Value is Rect bounds && bounds.IsFiniteBounded()
                    ? bounds
                    : null,
            },
            ChildrenCount = childrenCount,
            SubtreeTruncated = subtreeTruncated,
            Children = children,
        };
    }

    /// <summary>A RichEdit control terminates its value with a carriage return no user typed.</summary>
    private static string? Value(NodeProperties properties)
    {
        var value = properties.Text(PropertyIds.ValueValue);
        if (value is not null
            && value.EndsWith('\r')
            && properties.Text(PropertyIds.ClassName) is { } className
            && className.StartsWith("richedit", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^1];
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }
}
