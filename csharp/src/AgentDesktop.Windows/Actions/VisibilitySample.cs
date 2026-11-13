using AgentDesktop.Core.Tree;

namespace AgentDesktop.Windows.Actions;

/// <summary>Target bounds, provider offscreen flag, and the ancestor viewport read for one visibility check.</summary>
internal readonly record struct VisibilitySample(Rect? Bounds, bool? Offscreen, Rect? Viewport);
