namespace AgentDesktop.Core.Tree;

/// <summary>The per-slot read outcome the shared name precedence consumes. It carries no role
/// and no notion of child completeness on purpose. Both are platform concepts — a raw role
/// token, an adapter's own view of whether it enumerated every child — and threading either
/// through would put a platform token inside shared code. Each caller folds its own gating into
/// these statuses first, so the shared function is platform-neutral by construction.</summary>
public sealed record NameSlotStatus
{
    public SlotStatus ExplicitLabel { get; init; } = SlotStatus.Certain;
    public SlotStatus LabelledByText { get; init; } = SlotStatus.Certain;
    public SlotStatus NativeTitle { get; init; } = SlotStatus.Certain;
    public SlotStatus StaticValue { get; init; } = SlotStatus.Certain;
    public SlotStatus ChildLabel { get; init; } = SlotStatus.Certain;
    public SlotStatus Placeholder { get; init; } = SlotStatus.Certain;
    public SlotStatus Description { get; init; } = SlotStatus.Certain;
}
