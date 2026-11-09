using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Tree;
using AgentDesktop.Windows.Uia;
using Xunit;

namespace AgentDesktop.Windows.Tests;

/// <summary>A stub UIA node with deterministic properties and children so Walk and NodeBuilder
/// can be verified without a desktop session.</summary>
internal sealed class StubNode : IDisposable
{
    internal StubNode(NodeProperties properties, params StubNode[] children)
    {
        Properties = properties;
        Children = children.ToList();
    }

    internal NodeProperties Properties { get; }

    internal LabelOutcome Label { get; init; } = LabelOutcome.Unlabelled;

    internal List<StubNode> Children { get; }

    internal bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

/// <summary>Stub TreeSource that hands out StubNodes and reports their web-wrapper status.</summary>
internal sealed class StubTreeSource : TreeSource<StubNode>
{
    public NodeProperties Read(StubNode node) => node.Properties;

    public LabelOutcome ReadLabel(StubNode node) => node.Label;

    public int Children(StubNode node, bool countOnly, List<StubNode> into)
    {
        into.AddRange(node.Children);
        return 0;
    }

    public bool IsWebWrapper(NodeProperties properties) =>
        properties.Text(PropertyIds.ClassName) == "Chrome_RenderWidgetHostHWND";
}

/// <summary>Verifies NodeBuilder descriptor mapping and Walk logical/raw depth boundary rules.</summary>
public sealed class WalkParityTests
{
    private const int ControlTypeButton = 50000;
    private const int ControlTypeEdit = 50004;
    private const int ControlTypeGroup = 50026;
    private const int ControlTypeWindow = 50032;
    private const int ControlTypePane = 50033;

    [Fact]
    public void UnreadNameIsRecordedAsUnknownNotAsAbsent()
    {
        var node = NodeBuilder.Build(Props(ControlTypeEdit), LabelOutcome.Unlabelled, new List<AccessibilityNode>(), null, false);

        Assert.True(node.Identity.Name.IsUnknown());
        Assert.Null(node.Identity.Name.Value);
    }

    /// A name UIA returned as empty means the element has no name, which is a complete answer:
    /// the slot is absent, not unknown. A ref captured with it can be matched against another
    /// capture that also found no name.
    /// A ref captured with an unread name has no proof to re-identify against, so it is refused
    /// before the application is read, the same disposition as a ref that was not unique at
    /// capture.
    [Fact]
    public void UnreadNameIsRefusedAsCaptureProof()
    {
        var candidate = ElementResolve.CandidateIdentity(Props(ControlTypeButton), "w1");

        Assert.True(candidate.StableName.IsUnknown());
        Assert.False(candidate.MatchesCapture(candidate));
    }

    [Fact]
    public void ReadNameIsAcceptedAsCaptureProof()
    {
        var candidate = ElementResolve.CandidateIdentity(Props(ControlTypeButton, name: "Save"), "w1");

        Assert.True(candidate.StableName.IsKnown());
        Assert.True(candidate.MatchesCapture(candidate));
    }

    private static NodeProperties Props(
        int controlType,
        string? name = null,
        string? className = null,
        string? automationId = null,
        string? localizedControlType = null,
        string? helpText = null,
        string? ariaRole = null,
        string? fullDescription = null,
        bool isPassword = false)
    {
        var reads = new Dictionary<int, LocatorField<object>>
        {
            [PropertyIds.ControlType] = LocatorField<object>.Known(controlType),
            [PropertyIds.IsEnabled] = LocatorField<object>.Known(true),
            [PropertyIds.IsPassword] = LocatorField<object>.Known(isPassword),
        };
        if (name is not null) reads[PropertyIds.Name] = LocatorField<object>.Known(name);
        if (className is not null) reads[PropertyIds.ClassName] = LocatorField<object>.Known(className);
        if (automationId is not null) reads[PropertyIds.AutomationId] = LocatorField<object>.Known(automationId);
        if (localizedControlType is not null) reads[PropertyIds.LocalizedControlType] = LocatorField<object>.Known(localizedControlType);
        if (helpText is not null) reads[PropertyIds.HelpText] = LocatorField<object>.Known(helpText);
        if (ariaRole is not null) reads[PropertyIds.AriaRole] = LocatorField<object>.Known(ariaRole);
        if (fullDescription is not null) reads[PropertyIds.FullDescription] = LocatorField<object>.Known(fullDescription);
        return new NodeProperties(reads);
    }

    [Fact]
    public void NodeBuilderPopulatesDescriptionAndDescriptorsAndWithholdsPasswordValues()
    {
        var props = Props(
            ControlTypeEdit,
            name: "Search",
            automationId: "search-box",
            localizedControlType: "search box",
            helpText: "Type to filter",
            ariaRole: "searchbox",
            fullDescription: "Search messages");

        var node = NodeBuilder.Build(props, LabelOutcome.Unlabelled, new List<AccessibilityNode>(), null, false);

        Assert.Equal("Search messages", node.Identity.Description.Value);
        Assert.Equal("searchbox", node.Presentation.Descriptors.Subrole);
        Assert.Equal("search box", node.Presentation.Descriptors.RoleDescription);
        Assert.Equal("Type to filter", node.Presentation.Descriptors.Placeholder);

        var passwordProps = Props(
            ControlTypeEdit,
            name: "Password",
            helpText: "Secret hint",
            fullDescription: "Secret description",
            isPassword: true);

        var passwordNode = NodeBuilder.Build(passwordProps, LabelOutcome.Unlabelled, new List<AccessibilityNode>(), null, false);
        Assert.Null(passwordNode.Identity.Description.Value);
        Assert.Null(passwordNode.Presentation.Descriptors.Placeholder);
    }

    [Fact]
    public void WebWrapperDoesNotConsumeLogicalDepthAndBoundaryEmitsChildrenCountOnlyAtCutoff()
    {
        var grandchild = new StubNode(Props(ControlTypeButton, name: "Send"));
        var wrapper = new StubNode(
            Props(ControlTypePane, name: "Web", className: "Chrome_RenderWidgetHostHWND"),
            grandchild);
        var root = new StubNode(Props(ControlTypeWindow, name: "Outlook"), wrapper);

        var budget = new WalkBudget(MaxLogicalDepth: 1, MaxRawDepth: 10, MaxSiblings: 100, Deadline.Standard());
        var observed = new Walk<StubNode>(new StubTreeSource(), budget).Run(root);

        Assert.Null(observed.Root.ChildrenCount);
        Assert.Single(observed.Root.Children);
        var wrapperNode = observed.Root.Children[0];
        Assert.Null(wrapperNode.ChildrenCount);
        Assert.Single(wrapperNode.Children);
        var buttonNode = wrapperNode.Children[0];
        Assert.Equal("Send", buttonNode.Identity.Name.Value);
    }

    [Fact]
    public void LogicalDepthBoundaryStopsExpansionAndReportsChildrenCount()
    {
        var leaf = new StubNode(Props(ControlTypeButton, name: "Leaf"));
        var child = new StubNode(Props(ControlTypeGroup, name: "Section"), leaf);
        var root = new StubNode(Props(ControlTypeWindow, name: "App"), child);

        var budget = new WalkBudget(MaxLogicalDepth: 1, MaxRawDepth: 10, MaxSiblings: 100, Deadline.Standard());
        var observed = new Walk<StubNode>(new StubTreeSource(), budget).Run(root);

        Assert.Null(observed.Root.ChildrenCount);
        Assert.Single(observed.Root.Children);
        var section = observed.Root.Children[0];
        Assert.Empty(section.Children);
        Assert.Equal(1u, section.ChildrenCount);
        Assert.False(section.SubtreeTruncated);
    }
}