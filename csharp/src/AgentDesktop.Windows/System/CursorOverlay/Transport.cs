using global::System;
using global::System.Diagnostics;
using global::System.Runtime.InteropServices;
using global::System.Threading;
using Microsoft.Win32.SafeHandles;
using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Talking to a renderer that may or may not exist yet.
/// One connection carries one control and reads back at most one byte. The read never parks a thread:
/// <c>PeekNamedPipe</c> is polled to a deadline and <c>ReadFile</c> is called only once bytes are already waiting.
/// Polling keeps the property the decision was made for: no thread sits in a Win32 call past its own deadline with
/// nothing able to reclaim it. It needs no overlapped lifetime and no cancellation semantics.
/// </summary>
internal static class Transport
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPipeBusy = 231;
    private const uint MaxWaitMilliseconds = 1000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(4);

    internal static ReachOutcome Reach(string name, CursorOverlayControl control, TimeSpan budget)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ReachOutcome.NoRenderer;
        }

        byte[] payload;
        try
        {
            payload = Framing.Encode(control);
        }
        catch (AdapterError error)
        {
            return ReachOutcome.Unreachable(error);
        }

        return Deliver(name, payload, Framing.IsAcknowledged(control), budget);
    }

    private static ReachOutcome Deliver(string name, byte[] payload, bool expectsAcknowledgement, TimeSpan budget)
    {
        long deadline = DeadlineFrom(budget);
        using SafeFileHandle? handle = Connect(name, deadline, out ReachOutcome? failure);
        if (handle is null)
        {
            return failure ?? ReachOutcome.NoRenderer;
        }

        if (!Peer.ServerIsThisUser(handle))
        {
            return ReachOutcome.Unreachable(AdapterError.Internal(
                "The cursor overlay pipe is not served by this user's overlay renderer"));
        }

        bool wrote = WriteFile(handle, payload, (uint)payload.Length, out uint written, IntPtr.Zero);
        if (!wrote || written != (uint)payload.Length)
        {
            return ReachOutcome.Unreachable(Wide.Win32Error(
                "The cursor overlay control could not be written to its renderer"));
        }

        if (!expectsAcknowledgement)
        {
            return ReachOutcome.Delivered;
        }

        try
        {
            AwaitAcknowledgement(handle, deadline);
            return ReachOutcome.Delivered;
        }
        catch (AdapterError error)
        {
            return ReachOutcome.Unreachable(error);
        }
    }

    /// <summary>
    /// Opens the renderer's pipe, waiting out <c>ERROR_PIPE_BUSY</c> within the deadline.
    /// The wait floor is load-bearing: <c>WaitNamedPipeW</c> reads zero as <c>NMPWAIT_USE_DEFAULT_WAIT</c> and parks
    /// for the server's own default rather than returning, so a remaining budget that rounds to zero must still ask
    /// for a millisecond.
    /// </summary>
    private static SafeFileHandle? Connect(string name, long deadline, out ReachOutcome? failure)
    {
        while (true)
        {
            SafeFileHandle handle = CreateFileW(
                name,
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);
            if (!handle.IsInvalid)
            {
                failure = null;
                return handle;
            }

            int code = Marshal.GetLastPInvokeError();
            if (code == ErrorFileNotFound)
            {
                failure = ReachOutcome.NoRenderer;
                handle.Dispose();
                return null;
            }

            if (code != ErrorPipeBusy)
            {
                failure = ReachOutcome.Unreachable(Wide.Win32Error(
                    "The cursor overlay renderer could not be reached"));
                handle.Dispose();
                return null;
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                failure = ReachOutcome.Unreachable(
                    new AdapterError(ErrorCode.Timeout, "The cursor overlay renderer stayed busy for the whole budget")
                        .WithPlatformDetail("ERROR_PIPE_BUSY throughout"));
                return null;
            }

            TimeSpan remaining = RemainingFrom(deadline);
            uint waitMilliseconds = (uint)Math.Clamp((long)remaining.TotalMilliseconds, 1, MaxWaitMilliseconds);
            WaitNamedPipeW(name, waitMilliseconds);
        }
    }

    /// <summary>
    /// Polls for the byte rather than blocking for it. <c>ReadFile</c> is reached only once <c>PeekNamedPipe</c> has
    /// already seen data, so no thread waits inside the OS past this deadline.
    /// </summary>
    private static void AwaitAcknowledgement(SafeFileHandle handle, long deadline)
    {
        while (true)
        {
            bool peeked = PeekNamedPipe(handle, IntPtr.Zero, 0, IntPtr.Zero, out uint available, IntPtr.Zero);
            if (!peeked)
            {
                throw Wide.Win32Error("The cursor overlay renderer closed before acknowledging");
            }

            if (available > 0)
            {
                byte[] byteBuffer = new byte[1];
                bool ok = ReadFile(handle, byteBuffer, 1, out uint read, IntPtr.Zero);
                if (!ok)
                {
                    throw Wide.Win32Error("The cursor overlay acknowledgement could not be read");
                }

                if (read != 1 || byteBuffer[0] != Framing.Acknowledgement)
                {
                    throw UnexpectedAnswer(read, byteBuffer[0]);
                }

                return;
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                throw new AdapterError(
                    ErrorCode.Timeout,
                    "The cursor overlay renderer did not acknowledge within its budget");
            }

            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>
    /// A read that succeeded and answered the wrong thing is a framing failure, not an OS failure, so the detail names
    /// what came back rather than the last Win32 error, which after a successful <c>ReadFile</c> is zero or a leftover
    /// from an earlier call and reads as "Win32 error 0".
    /// The byte is only quoted when a byte was actually read; otherwise the detail would report the buffer's
    /// initialiser as the renderer's answer.
    /// </summary>
    private static AdapterError UnexpectedAnswer(uint read, byte answer)
    {
        string detail = read == 1
            ? $"read 1 byte 0x{answer:x2}, expected the acknowledgement 0x{Framing.Acknowledgement:x2}"
            : $"read {read} bytes, expected exactly 1 acknowledgement byte";
        return AdapterError.Internal(
            "The cursor overlay renderer answered something other than an acknowledgement")
            .WithPlatformDetail(detail);
    }

    private static long DeadlineFrom(TimeSpan budget)
    {
        return Stopwatch.GetTimestamp() + (long)(budget.TotalSeconds * Stopwatch.Frequency);
    }

    private static TimeSpan RemainingFrom(long deadline)
    {
        long ticks = Math.Max(0, deadline - Stopwatch.GetTimestamp());
        return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle file,
        byte[] buffer,
        uint numberOfBytesToWrite,
        out uint numberOfBytesWritten,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "ReadFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        SafeFileHandle file,
        byte[] buffer,
        uint numberOfBytesToRead,
        out uint numberOfBytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "PeekNamedPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(
        SafeFileHandle namedPipe,
        IntPtr buffer,
        uint bufferSize,
        IntPtr bytesRead,
        out uint totalBytesAvailable,
        IntPtr bytesLeftThisMessage);

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipeW(string name, uint timeout);

    internal enum ReachKind
    {
        Delivered,
        NoRenderer,
        Unreachable,
    }

    /// <summary>
    /// Why a connection attempt did not deliver, which the caller needs in order to decide whether a renderer exists
    /// at all. <see cref="Error"/> is set only for <see cref="ReachKind.Unreachable"/>.
    /// </summary>
    internal sealed record ReachOutcome(ReachKind Kind, AdapterError? Error = null)
    {
        internal static ReachOutcome Delivered { get; } = new(ReachKind.Delivered);

        internal static ReachOutcome NoRenderer { get; } = new(ReachKind.NoRenderer);

        internal static ReachOutcome Unreachable(AdapterError error) => new(ReachKind.Unreachable, error);
    }

}
