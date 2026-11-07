using AgentDesktop.Core.Response;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Adapter;

/// <summary>One live read of one element: identity, state, bounds, and the actions it advertises.</summary>
public sealed record LiveElement
{
    public LiveIdentity Identity { get; init; } = new();
    public ElementState State { get; init; } = new();
    public bool StatesComplete { get; init; }
    public Rect? Bounds { get; init; }
    public List<string> AvailableActions { get; init; } = new();
}

public static class LiveElementEvidence
{
    /// <summary>The answer for a live read that left a field the projection needs unread. Retryable: the node answered, a field did not.</summary>
    public static AdapterError Incomplete() => AdapterError.IncompleteLiveEvidence();

    /// <summary>Whether every slot a single-element live read must answer was observed: role, value, states, bounds, and available actions.</summary>
    public static bool EssentialEvidenceComplete(Locator.LocatorEvidence evidence) =>
        !evidence.Role.IsUnknown()
        && !evidence.Value.IsUnknown()
        && !evidence.States.IsUnknown()
        && !evidence.RefEvidence.Bounds.IsUnknown()
        && !evidence.RefEvidence.AvailableActions.IsUnknown();

    /// <summary>An absent action list reads as empty; an unread one is refused as incomplete evidence.</summary>
    public static List<string> KnownActions(Locator.LocatorField<List<string>> actions)
    {
        if (actions.Kind == Locator.LocatorFieldKind.Known)
        {
            return actions.Value!;
        }

        if (actions.Kind == Locator.LocatorFieldKind.Absent)
        {
            return new List<string>();
        }

        throw Incomplete();
    }
}
