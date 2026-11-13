namespace AgentDesktop.Windows.Actions;

/// <summary>Whether a failed UI Automation write surfaced an HRESULT or a sentinel code.</summary>
internal enum WriteFailureKind
{
    Hresult,
    Sentinel,
}
