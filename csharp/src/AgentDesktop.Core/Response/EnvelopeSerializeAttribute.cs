namespace AgentDesktop.Core.Response;

/// <summary>Marks a type whose JSON writer the source generator emits. The writer appends
/// fields in the order the type declares them, so the envelope's byte order is the type's
/// own order, not a reflection walk over metadata.</summary>
[System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
public sealed class EnvelopeSerializeAttribute : System.Attribute
{
}
