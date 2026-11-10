using AgentDesktop.Core.Input;
using AgentDesktop.Core.Tree;

namespace AgentDesktop.Core.Actions;

/// <summary>The identity the adapter validated and used for a delivery. A pointer target is the dispatch coordinate and does not prove which application received the input.</summary>
public sealed record OperationTarget
{
    public string Kind { get; init; } = "";
    public OperationWindow? Window { get; init; }
    public OperationApp? App { get; init; }
    public Point? Pointer { get; init; }
    public Point? From { get; init; }
    public Point? To { get; init; }
}

public sealed record OperationWindow(string Id, string Title, string Process);

public sealed record OperationApp(string Name, string Process);
