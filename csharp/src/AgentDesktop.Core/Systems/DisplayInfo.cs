using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Systems;

/// <summary>An active monitor: a stable id, its bounds in screen coordinates, whether it is the primary, and its effective scale.</summary>
[EnvelopeSerialize]
public sealed partial record DisplayInfo(string Id, Rect Bounds, bool IsPrimary, double Scale);
