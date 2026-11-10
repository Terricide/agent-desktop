using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree;

/// <summary>The limits one walk runs inside. Logical depth is what the caller asked for; raw
/// depth also counts the web wrappers logical depth skips, and caps a tree whose wrappers
/// would otherwise nest without bound. Mirrors the Copy struct WalkBudget in
/// crates/windows/src/tree/walk/walker.rs: the With* methods return a new value and never
/// change the one they were called on.</summary>
internal readonly record struct WalkBudget(byte MaxLogicalDepth, byte MaxRawDepth, int MaxSiblings, Deadline Deadline)
{
    internal const byte DefaultMaxRawDepth = 50;

    internal const int DefaultMaxSiblings = 10_000;

    internal WalkBudget(byte maxLogicalDepth, Deadline deadline)
        : this(maxLogicalDepth, DefaultMaxRawDepth, DefaultMaxSiblings, deadline)
    {
    }

    internal WalkBudget WithMaxRawDepth(byte maxRawDepth) => this with { MaxRawDepth = maxRawDepth };

    internal WalkBudget WithMaxSiblings(int maxSiblings) => this with { MaxSiblings = maxSiblings };

    internal static WalkBudget For(TreeOptions options, Deadline deadline) =>
        new(options.MaxDepth, deadline);
}
