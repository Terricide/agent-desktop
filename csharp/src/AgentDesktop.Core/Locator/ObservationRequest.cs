using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Locator;

/// <summary>Where an observation starts: a window, a native element, and the surface kind it roots.</summary>
public sealed record ObservationRoot(WindowInfo? Window, Adapter.NativeHandle? Element, Snapshot.SnapshotSurface Surface);

/// <summary>What to observe and the budget it must finish inside.</summary>
public sealed record ObservationRequest(Tree.TreeOptions Options, Wait.Deadline Deadline);

/// <summary>A tree one observation produced. A tree that ran out of budget is complete=false with the nodes it did see.</summary>
public sealed record ObservedTree(Tree.AccessibilityNode Root, bool Complete, bool Truncated, uint NodesObserved);
