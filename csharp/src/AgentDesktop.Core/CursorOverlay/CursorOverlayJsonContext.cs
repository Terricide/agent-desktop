using System.Text.Json.Serialization;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.CursorOverlay;

/// <summary>
/// The JSON metadata generated for the cursor overlay wire, so a renderer can
/// read and write controls without a runtime type model.
/// </summary>
[JsonSerializable(typeof(CursorOverlayControl))]
[JsonSerializable(typeof(CursorOverlayInstruction))]
[JsonSerializable(typeof(CursorOverlayStyle))]
[JsonSerializable(typeof(CursorMotionProfile))]
[JsonSerializable(typeof(CursorImages))]
[JsonSerializable(typeof(CursorImage))]
[JsonSerializable(typeof(Point))]
[JsonSerializable(typeof(Rect))]
public sealed partial class CursorOverlayJsonContext : JsonSerializerContext
{
}
