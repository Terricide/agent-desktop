using AgentDesktop.Core.Systems;

namespace AgentDesktop.Windows.System;

/// <summary>
/// The process identity a stored window or app evidence is corroborated against: the generation
/// token the kernel reports for a pid, and the image name the kernel reports for it. Both answers
/// come from the kernel, never from a pid alone, so a recycled pid is caught by the token and an
/// unnamed process is refused rather than guessed. Port of <c>ProcessIdentityCheck</c> in
/// <c>crates/windows/src/system/process_identity_check.rs</c>.
/// </summary>
internal static class ProcessIdentityCheck
{
    /// <summary>The generation token for a process, or nothing when the kernel will not open the
    /// process or will not report its creation time. A process with no token can never be
    /// corroborated, so a stored identity that needs one fails closed against it.</summary>
    internal static string? TokenForPid(ProcessId pid) => ProcessTimes.TokenForPid(pid.Value);

    /// <summary>Verifies a stored token against the process's current generation. A recycled pid
    /// whose process is a different generation fails closed, and a pid whose process is gone
    /// altogether reads no token and fails the same way.</summary>
    internal static bool MatchesInstance(ProcessId pid, string token) =>
        ProcessTimes.MatchesInstance(pid.Value, token);

    /// <summary>The image name the kernel reports for a process, or nothing when the kernel will not
    /// name it. An unreadable name never matches a stored application name, so a strict check fails
    /// closed on it, and a process that is not there at all is the same answer.</summary>
    internal static string? ProcessImageName(ProcessId? pid) =>
        pid is { } process ? ProcessSnapshot.Capture().ImageNameFor(process.Value) : null;
}
