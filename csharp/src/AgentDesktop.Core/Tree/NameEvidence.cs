namespace AgentDesktop.Core.Tree;

/// <summary>The name-evidence slots one platform filled. A slot that was not filled is null;
/// a slot that applies to this element is filled by the platform before the shared precedence
/// runs, so the precedence never sees a platform role token.</summary>
public sealed record NameEvidence
{
    public string? ExplicitLabel { get; init; }
    public string? LabelledByText { get; init; }
    public string? NativeTitle { get; init; }
    public string? StaticValue { get; init; }
    public string? ChildLabel { get; init; }
    public string? Placeholder { get; init; }
    public string? Description { get; init; }
}
