using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.Tree.Properties;

/// <summary>The value a property read returned. A UIA variant carries four shapes: a string, a bool, an
/// int, and a bounding rectangle. A variant of any other type is not a value, so the read that produced it
/// is recorded as Unknown rather than as a value of the wrong shape.</summary>
internal abstract record PropertyValue
{
    private PropertyValue()
    {
    }

    public sealed record Text(string Value) : PropertyValue;

    public sealed record Flag(bool Value) : PropertyValue;

    public sealed record Number(int Value) : PropertyValue;

    public sealed record Bounds(Rect Value) : PropertyValue;
}
