using AgentDesktop.Windows.Actions;

namespace AgentDesktop.Windows.Uia;

/// <summary>The ExpandCollapse pattern bound to a live element, the C# shape of Rust
/// `get_pattern::<UIExpandCollapsePattern>()`.</summary>
internal sealed class ExpandCollapsePattern : PatternBase, IExpandCollapsePattern
{
    internal const string Label = "UIExpandCollapsePattern";

    private ExpandCollapsePattern(IntPtr pointer)
        : base(pointer, Label)
    {
    }

    internal static object Create(IntPtr pointer) => new ExpandCollapsePattern(pointer);

    public void Expand() => Invoke(Slots.ExpandCollapsePatternExpand, "Expand");

    public void Collapse() => Invoke(Slots.ExpandCollapsePatternCollapse, "Collapse");
}
