using AgentDesktop.Core.Response;
using AgentDesktop.Core.Snapshot;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Locator;


/// <summary>Shallow-traversal and renderer-accessibility handling for a web-wrapped target.
/// Port of crates/core/src/locator/live_locator/observation_request.rs (ObservationMode).</summary>
public readonly record struct ObservationMode(bool Skeleton, bool ForceRendererAccessibility, bool IdentityOnly)
{
    public static ObservationMode Default() => new(false, false, false);
}

/// <summary>What to observe and the budget it must finish inside. Port of the Rust
/// ObservationRequest: depths, surface and mode all derive from the tree options.</summary>
public sealed record ObservationRequest(TreeOptions Options, Wait.Deadline Deadline)
{
    internal const byte SnapshotMaxRawDepth = 50;
    internal const byte SkeletonMaxLogicalDepth = 3;

    public byte MaxRawDepth => SnapshotMaxRawDepth;

    public byte MaxLogicalDepth => SnapshotLogicalDepth(Options);

    public SnapshotSurface Surface => Options.Surface;

    public ObservationMode ObservationMode => new(Options.Skeleton, Options.ForceRendererAccessibility, false);

    /// <summary>The logical depth a snapshot request presents: a skeleton is an overview and never
    /// presents more than three levels.</summary>
    public static byte SnapshotLogicalDepth(TreeOptions options) =>
        options.Skeleton ? Math.Min(options.MaxDepth, SkeletonMaxLogicalDepth) : options.MaxDepth;

    public ObservationRequest Validate()
    {
        if (MaxRawDepth < 1 || MaxRawDepth > 50)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "max_raw_depth must be between 1 and 50");
        }

        if (MaxLogicalDepth > MaxRawDepth)
        {
            throw new AdapterError(
                    ErrorCode.InvalidArgs,
                    $"max depth {MaxLogicalDepth} exceeds the supported maximum of {MaxRawDepth}")
                .WithSuggestion($"Use a max depth of {MaxRawDepth} or less, or drill into a subtree with snapshot --root <ref>");
        }

        if (ObservationMode.Skeleton && MaxLogicalDepth > SkeletonMaxLogicalDepth)
        {
            throw new AdapterError(ErrorCode.InvalidArgs, "skeleton observations support a maximum logical depth of 3");
        }

        return this;
    }
}

/// <summary>A tree one observation produced. A tree that ran out of budget is complete=false with the nodes it did see.</summary>
public sealed record ObservedTree(Tree.AccessibilityNode Root, bool Complete, bool Truncated, uint NodesObserved)
{
    /// <summary>Port of ObservedTree::is_complete: whether the observation read the whole tree it was asked for.</summary>
    public bool IsComplete() => Complete;
}
