using AgentDesktop.Core.Adapter;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows;

/// <summary>The Windows platform adapter. Observation comes from the tree layer; every
/// action, input and system method keeps core's refusal until its layer implements it.</summary>
public sealed class Adapter : TreeAdapter, PlatformAdapter
{
}
