using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Input;

/// <summary>Armed-and-counted state shared by every physical release guard. A click guard, a
/// drag guard and a key-release guard differ only in what corrective input they post on abort
/// and how they phrase the recovery hint; the state machine underneath, armed until the final
/// release posts and counting committed injections, is one behaviour, and the delivery report
/// it produces is a contract. Kept in one place so a guard added later inherits the report
/// instead of restating it, and so guards cannot drift apart on what an interrupted sequence
/// claims. Cleanup stays with each guard: only the guard knows whether the corrective post is a
/// button-up in place or a move back to the origin first.</summary>
internal sealed class ReleaseState
{
    private bool _armed;

    private int _delivered;

    internal void Arm() => _armed = true;

    internal void Disarm() => _armed = false;

    internal void MarkDelivered() => _delivered += 1;

    internal int DeliveredUnits() => _delivered;

    internal bool ShouldRelease() => _armed;

    /// <summary>Folds the delivery evidence into a public error. Nothing committed yet means
    /// the caller may retry freely; anything committed means the OS may have acted on it, so
    /// the error carries what was posted and never claims the corrective release was
    /// acknowledged, because `SendInput` cannot report that.</summary>
    internal AdapterError EnrichError(AdapterError error, string suggestion)
    {
        if (DeliveredUnits() == 0)
        {
            return error.WithDisposition(DeliverySemantics.NotDelivered());
        }

        var enriched = error.WithDisposition(DeliverySemantics.DeliveredUnverified());
        var details = string.IsNullOrEmpty(enriched.Details)
            ? new JsonObject()
            : JsonNode.Parse(enriched.Details)!.AsObject();
        details["delivered_events"] = DeliveredUnits();
        if (ShouldRelease())
        {
            details["emergency_release_posted"] = true;
            details["emergency_release_acknowledged"] = false;
        }

        return enriched.WithDetails(details).WithSuggestion(suggestion);
    }
}
