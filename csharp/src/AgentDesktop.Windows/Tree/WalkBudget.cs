using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.Tree;

/// <summary>The limits one walk runs inside. Logical depth is what the caller asked for; raw
/// depth also counts the web wrappers logical depth skips, and caps a tree whose wrappers
/// would otherwise nest without bound.</summary>
internal record struct WalkBudget(byte MaxLogicalDepth, byte MaxRawDepth, int MaxSiblings, Deadline Deadline)
{
    internal const byte DefaultMaxRawDepth = 50;

    internal const int DefaultMaxSiblings = 10_000;

    internal static WalkBudget For(TreeOptions options, Deadline deadline) =>
        new(options.MaxDepth, DefaultMaxRawDepth, DefaultMaxSiblings, deadline);
}
