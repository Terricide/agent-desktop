namespace AgentDesktop.Windows;

/// <summary>One hang-probe pass over a process's top-level windows. Kept free of Win32 so the two-signal threshold is testable on every host.</summary>
internal enum HangProbeResult
{
    Responsive,
    Hung,
}
