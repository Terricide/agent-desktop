namespace AgentDesktop.Core.Systems;

/// <summary>An operating-system process number.</summary>
public readonly record struct ProcessId(uint Value)
{
    public static ProcessId From(uint value) => new(value);

    public override string ToString() => Value.ToString();
}
