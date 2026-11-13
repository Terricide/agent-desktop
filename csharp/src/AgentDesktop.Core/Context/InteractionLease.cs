using AgentDesktop.Core.Wait;

namespace AgentDesktop.Core.Context;

/// <summary>How a command may send input to an application.</summary>
public enum InteractionPolicy
{
    Unspecified,
    Physical,
    Synthetic,
    Semantic,
}

/// <summary>The right to send input, held for one operation budget. Adapters release it before readback.</summary>
public sealed class InteractionLease : IDisposable
{
    private readonly Deadline _deadline;
    public InteractionLease(Deadline deadline, InteractionPolicy policy)
    {
        _deadline = deadline;
        Policy = policy;
    }

    public InteractionPolicy Policy { get; }

    public Deadline Deadline() => _deadline;

    public void Dispose()
    {
    }
}
