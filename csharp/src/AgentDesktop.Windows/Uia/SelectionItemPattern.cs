using AgentDesktop.Windows.Actions;

namespace AgentDesktop.Windows.Uia;

/// <summary>The SelectionItem pattern bound to a live element, the C# shape of Rust
/// `get_pattern::<UISelectionItemPattern>()`.</summary>
internal sealed class SelectionItemPattern : PatternBase, ISelectionItemPattern
{
    internal const string Label = "UISelectionItemPattern";

    private SelectionItemPattern(IntPtr pointer)
        : base(pointer, Label)
    {
    }

    internal static object Create(IntPtr pointer) => new SelectionItemPattern(pointer);

    public void Select() => Invoke(Slots.SelectionItemPatternSelect, "Select");
}
