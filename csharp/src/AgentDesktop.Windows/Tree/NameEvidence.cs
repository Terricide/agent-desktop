using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Tree.Properties;

namespace AgentDesktop.Windows.Tree;

/// <summary>Supplies the name-evidence slots this platform can fill, and computes nothing: the
/// name and description come from <see cref="AccName"/>, the one precedence in the product. The
/// class is named <c>NameFields</c> rather than <c>NameEvidence</c> so it does not shadow the
/// Core <c>NameEvidence</c> record inside this namespace.</summary>
internal static class NameFields
{
    /// <summary>The name and description this platform reports for one element. The name is the
    /// strong signal on Windows: UIA's Name is the provider's own computed accessible name, so a
    /// label relation is admitted only when Name has nothing to say, and is suppressed otherwise.</summary>
    internal static (LocatorField<string> Name, LocatorField<string> Description) Resolve(
        ElementProperties properties,
        LabelOutcome label)
    {
        var evidence = NameEvidenceFactory.EvidenceOf(properties, label);
        var status = NameEvidenceFactory.StatusOf(properties, label);
        return (AccName.ResolveName(evidence, status), AccName.ResolveDescription(evidence, status));
    }
}
