namespace AgentDesktop.Core.Tree;

/// <summary>A provider identifier: the kind of identifier and the value the provider reported.</summary>
[AgentDesktop.Core.Response.EnvelopeSerialize]
public sealed partial record ElementIdentifier(IdentifierKind Kind, string Value);
