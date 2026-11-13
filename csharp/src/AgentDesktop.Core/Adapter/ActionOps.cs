using AgentDesktop.Core.Actions;
using AgentDesktop.Core.Context;
using AgentDesktop.Core.Refs;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Adapter;

/// <summary>Acting on a resolved element. Every method defaults to a refusal.</summary>
public interface ActionOps
{
    /// <summary>The headed-session requirement for a saved ref, which may differ from the request's own.</summary>
    HeadedRequirement HeadedRequirementForRef(RefEntry entry, ActionRequest request, Deadline deadline) =>
        request.HeadedRequirement();

    HeadedRequirement HeadedRequirement(NativeHandle handle, ActionRequest request, Deadline deadline) =>
        request.HeadedRequirement();

    ActionResult ExecuteAction(NativeHandle handle, ActionRequest request, InteractionLease lease) =>
        throw AdapterError.NotSupported("execute_action");

    void ScrollIntoView(NativeHandle handle, InteractionLease lease) =>
        throw AdapterError.NotSupported("scroll_into_view");

    /// <summary>Prepare a ref for dispatch. An adapter returns a strictly verified handle only when its own scroll supplies independent evidence of the same native object's movement. Scrolling alone never authorizes rebinding a target.</summary>
    NativeHandle? ScrollRefIntoView(RefEntry entry, NativeHandle handle, InteractionLease lease)
    {
        ScrollIntoView(handle, lease);
        return null;
    }
}
