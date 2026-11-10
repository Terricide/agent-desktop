using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;
using AgentDesktop.Windows.Tree.Properties;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>Walking a subtree from an arbitrary root element, and the vocabulary seam that
/// interprets one element's read set. The entry point is an element, not a window handle: a
/// window handle is one root source among several, and drill-down from a stored ref re-enters
/// this same traversal rather than forking a second one. No ref is allocated here; core's
/// allocator is the only allocator in the product. The bounds, the enumeration surface and the
/// traversal live in <c>WalkBudget</c>, <c>TreeSource</c> and <c>Walk</c>.</summary>
internal static class Walker
{
    /// <summary>Walks a live UI Automation subtree from an arbitrary root element. The cache
    /// request and the root prefetch are owned here, so the whole walk reads through one
    /// consistent mechanism and the source is released on every exit, including a walk that
    /// throws.</summary>
    internal static WalkOutcome WalkUiaSubtree(
        Element root,
        ObservationRoot rootSource,
        WalkBudget budget,
        bool includeBounds)
    {
        var hr = Client.Create(out var client);
        if (hr < 0 || client is null)
        {
            throw UiaFailure.Error(hr, "create the UI Automation client");
        }

        using var source = UiaTreeSource.Create(client, includeBounds);
        var prepared = source.PrepareRoot(root);
        var outcome = WalkFromRoot(source, prepared, rootSource, budget);
        prepared.Dispose();
        return outcome;
    }

    /// <summary>Walks the subtree under <paramref name="root"/> inside the budget. The outcome
    /// carries the tree, the accounting the walk measured, and the enumeration faults it
    /// reported, so a genuine fault never becomes a truncated success. <paramref name="rootSource"/>
    /// names the surface the walk started from for callers that attach it to the
    /// observation.</summary>
    internal static WalkOutcome WalkFromRoot<TNode>(
        TreeSource<TNode> source,
        TNode root,
        ObservationRoot rootSource,
        WalkBudget budget)
        where TNode : class, IDisposable
    {
        var (tree, stats) = new Walk<TNode>(source, budget).Run(root);
        return new WalkOutcome(tree, stats, new List<AdapterError>());
    }

    /// <summary>The role vocabulary, resolved from the properties the walk already read. The seam
    /// takes the read set rather than the element because every input the vocabulary needs, the
    /// control type and the pattern availability that refines it, arrives in the same batch.
    /// Reading them again from the element would cost a round trip per node for values already
    /// in hand. A control type that failed to read is an unread role, not a role.</summary>
    internal static LocatorField<string> WalkRole(ElementProperties properties)
    {
        var role = Roles.Resolve(properties);
        return role == Roles.Unknown ? LocatorField<string>.Unknown() : LocatorField<string>.Known(role);
    }

    /// <summary>The available-actions vocabulary, resolved from the same read set. A failed
    /// affordance read set is unknown, not an element that advertises nothing.</summary>
    internal static LocatorField<List<string>> WalkAvailableActions(ElementProperties properties) =>
        AvailableActions.Resolve(properties) is { } actions
            ? LocatorField<List<string>>.Known(actions)
            : LocatorField<List<string>>.Unknown();

    /// <summary>The state vocabulary, which needs the resolved role: several UIA state sources are
    /// only meaningful on some roles, and <c>ToggleState</c> in particular means <c>checked</c>
    /// only on a toggleable role such as a checkbox or switch. A read set whose every health probe
    /// failed is unknown, not an element with no states. An unread role is refined as the unknown
    /// role.</summary>
    internal static LocatorField<List<string>> WalkStates(ElementProperties properties, LocatorField<string> role)
    {
        var states = NodeStates.Resolve(properties, role.Value ?? Roles.Unknown);
        return states is { } read ? LocatorField<List<string>>.Known(read) : LocatorField<List<string>>.Unknown();
    }

    /// <summary>Resolves every interpreted slot from one element's read set. One place, so the
    /// ordering dependency between them is visible: states needs the role, and the name needs the
    /// label relation the caller resolved.</summary>
    internal static ResolvedVocabulary WalkVocabulary(ElementProperties properties, LabelOutcome label)
    {
        var role = WalkRole(properties);
        var states = WalkStates(properties, role);
        var (name, description) = NameFields.Resolve(properties, label);
        return new ResolvedVocabulary(role, WalkAvailableActions(properties), states, name, description);
    }
}

/// <summary>What the cycle guard compares. UI Automation hands back a new element proxy for every
/// query, so pointer identity says nothing at all; the ancestor-path semantics transfer, the key
/// does not. The runtime id is the identity UI Automation itself publishes, and when a provider
/// will not supply one the guard falls back to asking the client to compare two elements.</summary>
internal abstract record NodeKey
{
    /// <summary>The runtime id UI Automation published for this element, compared by value.</summary>
    internal sealed record Runtime(int[] RuntimeIds) : NodeKey
    {
        public bool Equals(Runtime? other) =>
            other is not null && RuntimeIds.AsSpan().SequenceEqual(other.RuntimeIds);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var id in RuntimeIds)
            {
                hash.Add(id);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>No runtime id is available, so the guard compares elements through the source
    /// instead.</summary>
    internal sealed record Unavailable : NodeKey;
}

/// <summary>One completed walk: the tree, the accounting that travels with it, and the
/// enumeration faults it reported. Failures is populated when the tree is incomplete: a real
/// enumeration fault produces an incomplete tree and a structured error, never a truncated
/// success. The shared walk raises the fault, so the failure list is empty for a walk that
/// returned a tree.</summary>
internal sealed record WalkOutcome(ObservedTree Tree, WalkStats Stats, List<AdapterError> Failures);
