using AgentDesktop.Windows.Actions;

namespace AgentDesktop.Windows.Uia;

/// <summary>The Toggle pattern bound to a live element, the C# shape of Rust
/// `get_pattern::<UITogglePattern>()`.</summary>
internal sealed class TogglePattern : PatternBase, ITogglePattern
{
    internal const string Label = "UITogglePattern";

    private TogglePattern(IntPtr pointer)
        : base(pointer, Label)
    {
    }

    internal static object Create(IntPtr pointer) => new TogglePattern(pointer);

    public void Toggle() => Invoke(Slots.TogglePatternToggle, "Toggle");
}
