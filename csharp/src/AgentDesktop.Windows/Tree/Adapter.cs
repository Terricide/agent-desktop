using System.Text.Json.Nodes;
using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Locator;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Systems;
using AgentDesktop.Core.Tree;
using AgentDesktop.Core.Wait;
using AgentDesktop.Windows.Uia;

namespace AgentDesktop.Windows.Tree;

/// <summary>The observation capability of the Windows adapter: a window's accessibility tree, read
/// through UI Automation. The owner is verified on the handle before and after the walk, so a window
/// replaced by a recycled handle mid-observation cannot be reported as the window the caller asked
/// for. Every other observation method keeps core's refusal.</summary>
public sealed class TreeAdapter : ObservationOps
{
    /// <summary>A tree that ran out of budget is returned with complete=false and the nodes it did
    /// see. An enumeration fault fails the observation instead.</summary>
    public ObservedTree ObserveTree(ObservationRoot root, ObservationRequest request)
    {
        var window = WindowRoot.Validate(root, request.Options.Surface);
        var handle = WindowRoot.Handle(window);
        WindowRoot.VerifyOwner(handle, window);
        using var client = BoundedClient();
        using var source = UiaTreeSource.Create(client, request.Options.IncludeBounds);
        using var element = WindowRoot.Resolve(client, handle, request.Deadline);
        var walk = new Walk<Element>(source, WalkBudget.For(request.Options, request.Deadline));
        var observed = walk.Run(element);
        WindowRoot.VerifyOwner(handle, window);
        return observed;
    }

    /// <summary>The whole tree or an error: an observation that ran out of budget is a TIMEOUT here,
    /// never a partial tree.</summary>
    public AccessibilityNode GetTree(WindowInfo window, TreeOptions options, Deadline deadline) =>
        WholeTree(ObserveTree(
            new ObservationRoot(window, null, options.Surface),
            new ObservationRequest(options, deadline)));

    internal static AccessibilityNode WholeTree(ObservedTree observed)
    {
        if (!observed.Complete)
        {
            throw new AdapterError(ErrorCode.Timeout, "Accessibility observation was incomplete")
                .WithSuggestion("Retry with a larger timeout or a narrower tree depth")
                .WithDetails(new JsonObject { ["kind"] = "observation_incomplete", ["nodes_observed"] = observed.NodesObserved });
        }

        return observed.Root;
    }

    /// <summary>Observation is refused rather than run against a client whose calls cannot time out.</summary>
    private static Client BoundedClient()
    {
        var hr = Client.Create(out var client);
        return client ?? throw UiaFailure.Error(hr, "create a UI Automation client whose calls are bounded")
            .WithSuggestion("This build does not provide CUIAutomation8; observation is refused rather than run against a client that cannot time out");
    }
}
