using global::System;
using global::System.Collections;
using global::System.Collections.Generic;
using global::System.Diagnostics;
using global::System.Linq;
using global::System.Runtime.InteropServices;
using global::System.Runtime.Versioning;
using global::System.Threading;
using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;
using AgentDesktop.Core.Storage;
using AgentDesktop.Core.Wait;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Reaching the renderer for a session, and starting one when there is none.
/// Connect first, classify a failure before acting on it, and spawn only for a control that may bring a renderer
/// into existence. A <c>Disable</c> that spawned would start a renderer in order to tell it to stop, and dispatch
/// sends <c>Hide</c>/<c>Show</c> around every mutating command in a headed session, which would fork a detached
/// renderer per command.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class Spawn
{
    /// <summary>
    /// Matches the arrival ceiling core already imposes on a travel, and is what a fresh renderer has to get its
    /// window up inside.
    /// </summary>
    private static readonly TimeSpan EnableBudget = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Teardown is allowed longer, because a caller of <c>disable</c> is waiting for the window to be gone rather
    /// than for a frame to land.
    /// </summary>
    private static readonly TimeSpan TeardownBudget = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How long a reach must have left to be worth making at all. A reach handed what is nearly nothing does not
    /// fail fast: it connects, writes, and then gives up waiting for the acknowledgement a renderer that has just
    /// come up is about to send, reporting an acknowledgement timeout for what is really an exhausted start-up
    /// budget. <c>WaitNamedPipeW</c> is worse still: a wait of zero milliseconds means use the server's own
    /// default, so a remaining budget that rounds to zero would park past the deadline instead of inside it.
    /// </summary>
    private static readonly TimeSpan MinimumReach = TimeSpan.FromMilliseconds(16);

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(8);

    /// <summary>
    /// The retry pause doubles from <see cref="RetryInterval"/> up to this ceiling: a renderer that is merely slow
    /// to register its pipe is caught by the first few polls, and one that is not coming stops being asked sixty
    /// times a second for the rest of the budget.
    /// </summary>
    private static readonly TimeSpan MaximumRetryInterval = TimeSpan.FromMilliseconds(128);

    private const uint CreateUnicodeEnvironment = 0x00000400;

    private const uint CreateNoWindow = 0x08000000;

    private const uint DetachedProcess = 0x00000008;

    /// <summary>
    /// The stale-generation sweep runs here, before the reach on the current name, rather than on the spawn path
    /// below. A sweep further down would miss the case that motivates it: both generations alive at once, the
    /// current one answering, so the reach returns delivered and the spawn path is never taken while the earlier
    /// generation's window is still drawn.
    /// </summary>
    /// <remarks>
    /// It runs on <c>Disable</c> as well as <c>Enable</c>, because the broadcast reaches the endpoints
    /// <see cref="Discover"/> can name and a retired generation's pipe is not one of them. An operator ending a
    /// session means all of it; leaving the earlier generation's window drawn is the one outcome teardown must not
    /// produce.
    /// </remarks>
    internal static void Update(CursorOverlayControl control)
    {
        control.Validate();
        string root = StateRoot.FromEnvironment(
            () => global::System.Environment.GetEnvironmentVariable(StateRoot.EnvironmentVariable)).Path;
        TimeSpan budget = BudgetFor(control);

        if (SweepsRetiredGenerations(control))
        {
            Retire.Sweep(root, control.SessionId);
        }

        if (control.IsDisable)
        {
            Broadcast(root, control, budget);
            return;
        }

        string name = PipeName.Build(root, control.SessionId, control.AgentId);
        Transport.ReachOutcome outcome = Transport.Reach(name, control, budget);
        if (outcome.Kind == Transport.ReachKind.Delivered)
        {
            return;
        }

        if (outcome.Kind == Transport.ReachKind.Unreachable)
        {
            throw FailureOf(outcome);
        }

        if (!Framing.MaySpawn(control))
        {
            return;
        }

        StartRenderer(name, control, budget);
    }

    /// <summary>
    /// Stops every cursor the session is drawing, not just the one an agent id would name.
    /// </summary>
    /// <remarks>
    /// A <c>Disable</c> carries no agent, which is the contract rather than an oversight: an operator ending a
    /// session means all of it, and the agents that drew inside it are not theirs to enumerate. So the endpoints
    /// are discovered and each is told to stop. The first refusal is reported and the rest are still attempted.
    /// Returning at the first one would leave the remaining cursors on screen while the caller was told teardown
    /// failed, which is the worst of both: an error, and the windows still up.
    /// </remarks>
    private static void Broadcast(string root, CursorOverlayControl control, TimeSpan budget)
    {
        AdapterError? firstError = null;
        foreach (string name in Discover.SessionEndpoints(root, control.SessionId))
        {
            Transport.ReachOutcome outcome = Transport.Reach(name, control, budget);
            if (outcome.Kind == Transport.ReachKind.Unreachable)
            {
                firstError ??= outcome.Error;
            }
        }

        if (firstError is not null)
        {
            throw firstError;
        }
    }

    /// <summary>
    /// Which controls clear retired protocol generations. Establishing a session and ending one both have to. A
    /// control inside a live session does not, and sweeping on those would reach retired pipes on every mutating
    /// command.
    /// </summary>
    private static bool SweepsRetiredGenerations(CursorOverlayControl control)
    {
        return control.IsEnable || control.IsDisable;
    }

    private static TimeSpan BudgetFor(CursorOverlayControl control)
    {
        return control.IsDisable ? TeardownBudget : EnableBudget;
    }

    /// <summary>
    /// Reaches until a renderer answers or the budget is gone, handing each attempt what is actually left rather
    /// than the full budget again.
    /// </summary>
    /// <remarks>
    /// Re-issuing the whole budget per attempt is not a rounding error: one reach that spends its ceiling waiting
    /// on a renderer that never came up is followed by another with the same ceiling, so a caller told
    /// <c>rendered: false</c> has waited roughly twice the arrival timeout core promised it. The loop always leaves
    /// by the start-up timeout rather than by a transport error, because "the renderer did not come up" is what a
    /// caller can act on; which Win32 call the last doomed attempt happened to give up in is not.
    /// </remarks>
    private static void RetryUntilReached(long deadline, Func<TimeSpan, Transport.ReachOutcome> reach)
    {
        TimeSpan pause = RetryInterval;
        while (true)
        {
            TimeSpan remaining = RemainingFrom(deadline);
            if (remaining < MinimumReach)
            {
                throw new AdapterError(
                    ErrorCode.Timeout,
                    "The cursor overlay renderer did not come up within its budget");
            }

            Transport.ReachOutcome outcome = reach(remaining);
            if (outcome.Kind == Transport.ReachKind.Delivered)
            {
                return;
            }

            if (outcome.Kind == Transport.ReachKind.Unreachable)
            {
                throw FailureOf(outcome);
            }

            Thread.Sleep(pause < remaining ? pause : remaining);
            TimeSpan doubled = pause + pause;
            pause = doubled < MaximumRetryInterval ? doubled : MaximumRetryInterval;
        }
    }

    private static TimeSpan RemainingFrom(long deadline)
    {
        long ticks = Math.Max(0, deadline - Deadline.Now());
        return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
    }

    private static AdapterError FailureOf(Transport.ReachOutcome outcome)
    {
        return outcome.Error
            ?? AdapterError.FromCode(ErrorCode.Internal, "The cursor overlay renderer could not be reached");
    }

    private static void StartRenderer(string name, CursorOverlayControl control, TimeSpan budget)
    {
        string image = Executable();
        string commandLine = "\"" + image + "\" "
            + string.Join(" ", PipeName.BuildChildArguments(control.SessionId, control.AgentId));
        char[] commandBuffer = Wide.Terminated(commandLine);
        ushort[] environment = ChildEnvironment();

        STARTUPINFOW startup = default;
        startup.cb = (uint)Marshal.SizeOf<STARTUPINFOW>();
        GCHandle pinned = GCHandle.Alloc(environment, GCHandleType.Pinned);
        try
        {
            bool started = CreateProcessW(
                null,
                commandBuffer,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                DetachedProcess | CreateNoWindow | CreateUnicodeEnvironment,
                pinned.AddrOfPinnedObject(),
                null,
                ref startup,
                out PROCESS_INFORMATION information);
            if (!started)
            {
                throw Wide.Win32Error("The cursor overlay renderer could not be started");
            }

            CloseHandle(information.hThread);
            CloseHandle(information.hProcess);
        }
        finally
        {
            pinned.Free();
        }

        AwaitRenderer(name, control, budget);
    }

    /// <summary>
    /// Spawns only from the CLI's own image. Under an FFI host the running executable is the host process, which
    /// must never be re-launched as a renderer, and a test binary must not fork one either. The refusal is an
    /// error rather than success, because dispatch turns success into <c>rendered: true</c>, which would claim an
    /// overlay that was never started. The image test is the one a client applies to a pipe server it found, so
    /// what may be forked and what may be trusted cannot drift apart.
    /// </summary>
    private static string Executable()
    {
        string path = global::System.Environment.ProcessPath
            ?? throw AdapterError.FromCode(
                ErrorCode.Internal,
                "The running executable could not be resolved");
        if (!ImageIdentity.IsAgentDesktopImage(path))
        {
            string stem = ImageIdentity.ImageStemOf(path) ?? string.Empty;
            throw AdapterError.NotSupported("update_cursor_overlay").WithPlatformDetail(
                $"the overlay renderer is only started from the {ImageIdentity.ImageStem} image, not '{stem}'");
        }

        return path;
    }

    /// <summary>
    /// Starts the renderer with no inherited handles at all. This is why the spawn is a raw <c>CreateProcessW</c>
    /// rather than a process builder: a builder passes <c>bInheritHandles = TRUE</c> whenever it configures stdio,
    /// and does not restrict what is inherited. A detached renderer that inherits its caller's stdout keeps that
    /// pipe open for its whole life, so any shell reading the command's output blocks until the overlay is torn
    /// down. That was measured, and it is why this path does not mirror the macOS spawn.
    /// </summary>
    /// <remarks>
    /// Losing the inherited stdin costs nothing: the control that started the child is delivered again over the
    /// pipe by <see cref="AwaitRenderer"/>, which has to connect anyway to read the acknowledgement.
    /// </remarks>
    private static void AwaitRenderer(string name, CursorOverlayControl control, TimeSpan budget)
    {
        long deadline = Deadline.ExpiryFor((long)budget.TotalMilliseconds);
        RetryUntilReached(deadline, remaining => Transport.Reach(name, control, remaining));
    }

    /// <summary>
    /// This process's environment plus the marker that turns the child into a renderer, as the
    /// double-null-terminated block <c>CreateProcessW</c> wants.
    /// </summary>
    private static ushort[] ChildEnvironment()
    {
        List<ushort> block = new();
        foreach (DictionaryEntry entry in global::System.Environment.GetEnvironmentVariables())
        {
            string key = entry.Key.ToString() ?? string.Empty;
            string value = entry.Value?.ToString() ?? string.Empty;
            if (key.Equals(PipeName.ChildMarker, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            block.AddRange((key + "=" + value).Select(c => (ushort)c));
            block.Add(0);
        }

        block.AddRange((PipeName.ChildMarker + "=" + PipeName.ProtocolGeneration).Select(c => (ushort)c));
        block.Add(0);
        block.Add(0);
        return block.ToArray();
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? applicationName,
        char[] commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref STARTUPINFOW startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }
}
