namespace AgentDesktop.Core.Tree;

/// <summary>The live state of one element: role, states, value, and the visibility flags a read proved.</summary>
public sealed record ElementState
{
    public string Role { get; init; } = "";
    public List<string> States { get; init; } = new();
    public string? Value { get; init; }
    public bool? Enabled { get; init; }
    public bool? Hidden { get; init; }
    public bool? Offscreen { get; init; }

    /// <summary>Visibility the live state alone proves when bounds cannot be read: known on-screen and not hidden is visible, hidden is not, anything else proves nothing.</summary>
    public bool? VisibleWithoutBounds()
    {
        if (Offscreen != false)
        {
            return null;
        }

        var hidden = Hidden ?? TreeStates.Has(States, TreeStates.Hidden);
        return !hidden;
    }
}
