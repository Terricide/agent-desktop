namespace AgentDesktop.Core.Input;

/// <summary>A keyboard chord: one key with the modifiers held with it.</summary>
public sealed record KeyCombo
{
    public string Key { get; init; } = "";
    public List<Modifier> Modifiers { get; init; } = new();
}
