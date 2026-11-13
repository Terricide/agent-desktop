using AgentDesktop.Windows.Actions;

namespace AgentDesktop.Windows.Uia;

/// <summary>The Scroll pattern bound to a live element, the C# shape of Rust
/// `get_pattern::<UIScrollPattern>()`. Supplies the two axis flags, the two percents, and
/// the Scroll call the ladder drives, read through the slots the specification fixes.</summary>
internal sealed class ScrollPattern : PatternBase, Scroll.IScrollPattern
{
    internal const string Label = "UIScrollPattern";

    private ScrollPattern(IntPtr pointer)
        : base(pointer, Label)
    {
    }

    internal static object Create(IntPtr pointer) => new ScrollPattern(pointer);

    public bool IsVerticallyScrollable() =>
        ReadFlag(Slots.ScrollPatternGetCurrentVerticallyScrollable, "IsVerticallyScrollable");

    public bool IsHorizontallyScrollable() =>
        ReadFlag(Slots.ScrollPatternGetCurrentHorizontallyScrollable, "IsHorizontallyScrollable");

    public double VerticalScrollPercent() =>
        ReadPercent(Slots.ScrollPatternGetCurrentVerticalScrollPercent, "VerticalScrollPercent");

    public double HorizontalScrollPercent() =>
        ReadPercent(Slots.ScrollPatternGetCurrentHorizontalScrollPercent, "HorizontalScrollPercent");

    public void Scroll(Scroll.ScrollAmount horizontal, Scroll.ScrollAmount vertical) =>
        InvokeWithInts(Slots.ScrollPatternScroll, "Scroll", (int)horizontal, (int)vertical);
}
