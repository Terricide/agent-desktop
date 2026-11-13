namespace AgentDesktop.Core.Snapshot;

/// <summary>Observation budget for one snapshot. Running out of budget is a complete
/// answer that says it is partial, never a failure.</summary>
public sealed class SnapshotBudget
{
    public SnapshotBudget(int maxNodes)
    {
        MaxNodes = maxNodes;
    }

    public int MaxNodes { get; }

    public int NodesObserved { get; private set; }

    public bool IsComplete => NodesObserved < MaxNodes;

    public bool TryObserve()
    {
        if (NodesObserved >= MaxNodes)
        {
            return false;
        }
        NodesObserved++;
        return true;
    }
}
