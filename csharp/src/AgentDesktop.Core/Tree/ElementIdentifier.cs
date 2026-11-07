namespace AgentDesktop.Core.Tree;

/// <summary>A provider-supplied element identifier, paired with the kind it came from.</summary>
public sealed record ElementIdentifier(IdentifierKind Kind, string Value);
