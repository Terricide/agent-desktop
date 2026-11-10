using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows;

/// <summary>Process-state rules for Windows. An exit code with the 0xC nibble is a crash; anything else is an exit.</summary>
internal static class ProcessStateRules
{
    private const uint CrashNibbleMask = 0xF000_0000;
    private const uint CrashNibbleValue = 0xC000_0000;

    /// <summary>Maps a wait-confirmed exit code onto Exited or Crashed. The wait gate, not this rule, decides whether the process is gone.</summary>
    internal static ProcessState StateFromExitCode(uint code) =>
        (code & CrashNibbleMask) == CrashNibbleValue
            ? ProcessState.CrashedWith(unchecked((int)code))
            : ProcessState.ExitedWith(unchecked((int)code));

    /// <summary>Reports Unresponsive only when two consecutive probes both see a hung window. One transient hung pass stays Running.</summary>
    internal static ProcessState ClassifyHang(Func<HangProbeResult> probe) =>
        probe() switch
        {
            HangProbeResult.Responsive => ProcessState.Running,
            _ => probe() switch
            {
                HangProbeResult.Responsive => ProcessState.Running,
                _ => ProcessState.Unresponsive,
            },
        };
}
