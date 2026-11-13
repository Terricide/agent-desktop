using System.Runtime.Versioning;

/// <summary>
/// The Windows adapter is built for Windows alone: the Rust original gates this crate by target operating system,
/// and every surface in it reaches UI Automation, GDI, and the window manager. Declaring the platform once for the
/// assembly is what lets the analyser accept those calls rather than flagging each call site as reachable on
/// platforms this project is never built for.
/// </summary>
[assembly: SupportedOSPlatform("windows")]
