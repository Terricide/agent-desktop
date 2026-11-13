namespace AgentDesktop.Core.Systems;

/// <summary>Liveness and responsiveness of a target process. Exited carries an optional exit code; Crashed carries the signal or NTSTATUS-shaped code.</summary>
public sealed record ProcessState(ProcessStateKind Kind, int? Code = null, int? SignalOrCode = null)
{
    public static ProcessState Running { get; } = new(ProcessStateKind.Running);

    public static ProcessState Unresponsive { get; } = new(ProcessStateKind.Unresponsive);

    public static ProcessState ExitedWith(int? code) => new(ProcessStateKind.Exited, Code: code);

    public static ProcessState CrashedWith(int signalOrCode) => new(ProcessStateKind.Crashed, SignalOrCode: signalOrCode);

    /// <summary>Compact lowercase tag for best-effort process-state enrichment on terminal errors.</summary>
    public string Label() => Kind switch
    {
        ProcessStateKind.Running => "running",
        ProcessStateKind.Exited => "exited",
        ProcessStateKind.Crashed => "crashed",
        ProcessStateKind.Unresponsive => "unresponsive",
        _ => Kind.ToString().ToLowerInvariant(),
    };
}
