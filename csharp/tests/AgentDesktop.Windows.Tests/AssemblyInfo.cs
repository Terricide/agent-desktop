using System.Runtime.Versioning;

/// <summary>
/// The tests drive the Windows adapter, which is built for Windows alone, so the test assembly
/// declares the same platform support as the assembly under test. Declaring it once here is what
/// lets the analyser accept a call into the adapter rather than flagging every call site as
/// reachable on platforms this project is never built for.
/// </summary>
[assembly: SupportedOSPlatform("windows")]

/// <summary>
/// xUnit runs test classes in parallel by default. These classes share one desktop session: the
/// cursor-overlay hosts create real tool windows owned by this process, and a menu probe enumerates
/// exactly those, so a host standing in a parallel class makes the probe read a window that is not
/// pumping messages. Foreground, focus, and input synthesis are process- and session-wide for the
/// same reason. One class runs at a time.
/// </summary>
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
