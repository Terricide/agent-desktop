using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Removes a renderer left drawing by a generation this build cannot address.
/// The control pipe's name hashes the protocol generation, so a renderer from an earlier generation is
/// unreachable rather than merely out of date. Its window stays topmost, click-through and absent from the
/// taskbar, and every control this build sends routes past it. The session-manifest watch does not help
/// because the session has not ended.
/// Stale generations are read from the ledger, never from the running processes: inspecting another process's
/// command line costs hundreds of milliseconds on a path that runs before every overlay is enabled, and a pipe
/// name is a pure function of the generation.
/// </summary>
internal static class Retire
{
    /// <summary>
    /// Short on purpose. The sweep runs on the enable path ahead of the reach that decides `data.rendered`, so
    /// whatever it spends is spent by every enable. A renderer that will answer answers in milliseconds; one that
    /// has not answered inside this budget is one the forced termination exists for.
    /// </summary>
    internal static readonly TimeSpan RetirementBudget = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Clears every renderer this build can no longer address, for this session.
    /// Silent by design. A session with no stale renderer is the ordinary case, and a sweep that fails must not
    /// turn a working enable into an error: the overlay is fail-soft and the caller asked to draw, not to tidy up.
    /// </summary>
    internal static void Sweep(string root, string sessionId)
    {
        foreach (var name in RetirementTargets(root, sessionId, PipeName.ProtocolGenerations))
        {
            RetireOne(name, sessionId);
        }
    }

    /// <summary>
    /// The pipe names a sweep must clear: one per retired generation, and never the one this build is about to use.
    /// Pure, so what the sweep aims at can be asserted without a renderer to aim at.
    /// </summary>
    internal static IReadOnlyList<string> RetirementTargets(string root, string sessionId, IReadOnlyList<string> ledger)
    {
        return PipeName.RetiredGenerations(ledger)
            .Select(generation => PipeName.ForGeneration(root, sessionId, null, generation))
            .ToList();
    }

    /// <summary>
    /// Politely first. A stale renderer that can still decode a `Disable` takes itself down and its
    /// acknowledgement says so; only one that answers nothing usable inside the budget is ended by force.
    /// <c>NoRenderer</c> means the name is not served at all, so there is no server to resolve and nothing to end.
    /// </summary>
    private static void RetireOne(string name, string sessionId)
    {
        var disable = CursorOverlayControl.Disable(sessionId);
        var outcome = Transport.Reach(name, disable, RetirementBudget);
        if (outcome.Kind == Transport.ReachKind.Unreachable)
        {
            TerminateServerOf(name);
        }
    }

    /// <summary>
    /// Opens the pipe only to learn who serves it, so read access is enough: nothing is written down a connection
    /// whose server is about to end. The handle is released after the termination request.
    /// </summary>
    private static void TerminateServerOf(string name)
    {
        using var handle = Connect(name);
        if (handle is null)
        {
            return;
        }

        Peer.TerminatePipeServer(handle);
    }

    /// <summary>
    /// Every instance being busy still means a server is there, which is the case worth waiting a moment for. The
    /// wait is never handed zero: <c>WaitNamedPipeW</c> reads zero as "use the server's own default wait", which
    /// would park past this deadline rather than inside it.
    /// </summary>
    private static SafeFileHandle? Connect(string name)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var handle = NativePipe.CreateFileW(
                name,
                NativePipe.GenericRead,
                NativePipe.FileShareRead | NativePipe.FileShareWrite,
                IntPtr.Zero,
                NativePipe.OpenExisting,
                0,
                IntPtr.Zero);
            var error = Marshal.GetLastWin32Error();
            if (!handle.IsInvalid)
            {
                return handle;
            }

            handle.Dispose();
            if (error != NativePipe.ErrorPipeBusy)
            {
                return null;
            }

            var remaining = RetirementBudget - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            var wait = Math.Clamp((uint)remaining.TotalMilliseconds, 1u, 1000u);
            NativePipe.WaitNamedPipeW(name, wait);
        }
    }

    /// <summary>
    /// Kernel32 pipe entry points and the constants the connect loop uses.
    /// </summary>
    private static class NativePipe
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const int ErrorPipeBusy = 231;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        internal static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "WaitNamedPipeW")]
        internal static extern bool WaitNamedPipeW(string name, uint timeout);
    }
}
