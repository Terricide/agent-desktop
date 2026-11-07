namespace AgentDesktop.Core.Tree;

/// <summary>The state vocabulary a live read reports in its states list.</summary>
public static class TreeStates
{
    public const string Hidden = "hidden";

    public static bool Has(List<string> states, string wanted) => states.Contains(wanted);
}
