namespace AgentDesktop.Windows.Tree;

/// <summary>The verified process identity a resolved element carries, so the live read can corroborate that the
/// provider is still the process the resolution verified.</summary>
internal sealed record ProcessPayload(uint Pid, string Token);
