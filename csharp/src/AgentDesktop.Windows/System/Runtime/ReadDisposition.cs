using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>How a failed read is classified for the resolution retry loop.</summary>
/// <remarks>
/// <c>SettledAbsence</c> is structurally impossible (the provider answered that the property or pattern does
/// not exist, or the request could not be valid) and is never retried. <c>Retryable</c> covers transport,
/// timeout, or a provider that changed under the read. <c>Unavailable</c> is a vanished element: stale for the
/// resolved target, incomplete for a node vanishing mid-descent. <c>Terminal</c> is a hard failure with no
/// defined recovery.
/// </remarks>
internal enum ReadDisposition
{
    SettledAbsence,
    Retryable,
    Unavailable,
    Terminal,
}
