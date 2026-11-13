namespace AgentDesktop.Core.Actions;

/// <summary>An action a command asks an element to perform, with the headed-session context the adapter must check.</summary>
public sealed record ActionRequest(Action Action, bool Headed)
{
    public HeadedRequirement HeadedRequirement() => Action.Requirement();
}
