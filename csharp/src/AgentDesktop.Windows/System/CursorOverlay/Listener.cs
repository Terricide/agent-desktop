using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using AgentDesktop.Core.CursorOverlay;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// The renderer's end of the control pipe.
/// </summary>
/// <remarks>
/// <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> is the singleton lock: a second child fails to claim the name and withdraws,
/// which is why claiming happens before any window exists. <c>PIPE_REJECT_REMOTE_CLIENTS</c> keeps the channel local,
/// and every accepted connection is checked against this user before its payload is read.
/// <para>
/// Accepting happens on a worker thread, and the reason is that <c>ConnectNamedPipe</c> on a synchronous pipe does
/// not return until a client arrives. A main loop calling it directly could never notice anything else — not a
/// window message, not the end of its own session — so its idle branch would exist without ever being reachable. The
/// worker blocks; the thread that owns the window stays free to tick.
/// </para>
/// <para>
/// Acknowledging and disconnecting stay on the main thread, after the frame has been drawn. A worker that
/// acknowledged would race the process exit a <c>Disable</c> triggers, and a teardown that had in fact succeeded
/// would report a broken pipe to the caller waiting on it.
/// </para>
/// </remarks>
internal sealed class Listener
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(8);
    private static readonly TimeSpan PayloadWait = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan AcknowledgementBudget = TimeSpan.FromMilliseconds(250);

    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FirstPipeInstance = 0x00080000;
    private const uint PipeTypeByte = 0;
    private const uint PipeReadModeByte = 0;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const int ErrorAccessDenied = 5;
    private const int ErrorPipeConnected = 535;

    private readonly SafeFileHandle _handle;
    private readonly BlockingCollection<CursorOverlayControl> _arrivals = new(1);
    private readonly BlockingCollection<ReleaseToken> _release = new(1);
    private int _controlWaiting;

    private Listener(SafeFileHandle handle)
    {
        _handle = handle;
    }

    /// <summary>
    /// Claims the pipe name. A second renderer that cannot claim it withdraws here, before a window exists, which is
    /// what keeps a duplicate from drawing.
    /// </summary>
    internal static Listener Claim(string name)
    {
        char[] pipeName = Wide.Terminated(name);
        IntPtr handle = CreateNamedPipeW(
            pipeName,
            PipeAccessDuplex | FirstPipeInstance,
            PipeTypeByte | PipeReadModeByte | PipeRejectRemoteClients,
            1,
            (uint)Framing.MaxControlBytes,
            (uint)Framing.MaxControlBytes,
            0,
            IntPtr.Zero);
        if (handle == InvalidHandle)
        {
            int code = Marshal.GetLastPInvokeError();
            if (code == ErrorAccessDenied)
            {
                throw ClaimException.AlreadyServed();
            }

            throw ClaimException.Failed(Wide.Win32Error(
                "The cursor overlay pipe could not be created"));
        }

        Listener listener = new(new SafeFileHandle(handle, ownsHandle: false));
        Thread worker = new(() => AcceptLoop(listener._handle, listener._arrivals, listener._release, listener));
        worker.IsBackground = true;
        worker.Start();
        return listener;
    }

    /// <summary>
    /// Waits up to <paramref name="tick"/> for the worker to hand over a control. An idle return is a real tick of
    /// quiet, which is what lets the caller pump its window and re-read its session.
    /// </summary>
    internal Accepted NextControl(TimeSpan tick)
    {
        if (_arrivals.TryTake(out CursorOverlayControl? control, (int)tick.TotalMilliseconds) && control is not null)
        {
            Volatile.Write(ref _controlWaiting, 0);
            return new ControlAccepted(control);
        }

        return new IdleAccepted();
    }

    /// <summary>
    /// Writes the acknowledgement and waits for the client to take it.
    /// </summary>
    /// <remarks>
    /// The flush is not tidiness. Disconnecting discards whatever the client has not read, and the one control that
    /// acknowledges and then immediately tears down is <c>Disable</c> — so without this, the caller waiting on a
    /// teardown that did succeed would see a broken pipe. The waiting is bounded, for the reason the helper gives.
    /// </remarks>
    internal void Acknowledge()
    {
        byte[] byteBuffer = [Framing.Acknowledgement];
        if (!WriteFile(_handle.DangerousGetHandle(), byteBuffer, 1, out _, IntPtr.Zero))
        {
            return;
        }

        WaitForTheClientToRead();
    }

    /// <summary>
    /// Releases the connection and lets the worker wait for the next client. Without the disconnect the pipe stays
    /// bound to a departed client and no second control is ever accepted.
    /// </summary>
    internal void Finish()
    {
        DisconnectNamedPipe(_handle.DangerousGetHandle());
        _release.Add(new ReleaseToken());
    }

    /// <summary>
    /// Whether a control has already been read off the wire and is waiting to be served.
    /// </summary>
    /// <remarks>
    /// A flourish holds the renderer for the better part of a second, and the sender of the next control has less
    /// than that before it gives up, so the flourish asks whether anything is waiting and gives way when there is.
    /// </remarks>
    internal bool ControlWaiting() => Volatile.Read(ref _controlWaiting) != 0;

    /// <summary>
    /// Waits for the acknowledgement to be taken, but never past the budget.
    /// </summary>
    /// <remarks>
    /// <c>FlushFileBuffers</c> on a pipe server returns only once the client has read everything, and it takes no
    /// timeout — so calling it on this thread hands a client the ability to park the renderer forever simply by not
    /// reading. Parked here, the window is never pumped, no further control is ever accepted, and the session watch
    /// never runs. So the wait happens on a thread that is allowed to be abandoned; giving up costs only the
    /// acknowledgement, which the client is already prepared to time out on.
    /// </remarks>
    private void WaitForTheClientToRead()
    {
        ParkedFlush? parked = ParkedFlush.Claim();
        if (parked is null)
        {
            return;
        }

        BlockingCollection<ReleaseToken> done = new(1);
        Thread flusher = new(() =>
        {
            using (parked)
            {
                FlushFileBuffers(_handle.DangerousGetHandle());
                done.Add(new ReleaseToken());
            }
        });
        flusher.IsBackground = true;
        flusher.Start();
        done.TryTake(out _, (int)AcknowledgementBudget.TotalMilliseconds);
    }

    /// <summary>
    /// The accept loop. <c>waiting</c> is raised before the hand-over rather than after it, so it is already true for
    /// the whole time the serving thread spends finishing the previous control.
    /// </summary>
    private static void AcceptLoop(
        SafeFileHandle handle,
        BlockingCollection<CursorOverlayControl> arrivals,
        BlockingCollection<ReleaseToken> release,
        Listener owner)
    {
        while (true)
        {
            int connected = ConnectNamedPipe(handle.DangerousGetHandle(), IntPtr.Zero);
            if (connected == 0 && Marshal.GetLastPInvokeError() != ErrorPipeConnected)
            {
                return;
            }

            if (!Peer.PeerIsThisUser(handle))
            {
                DisconnectNamedPipe(handle.DangerousGetHandle());
                continue;
            }

            CursorOverlayControl? control = ReadControl(handle);
            if (control is null)
            {
                DisconnectNamedPipe(handle.DangerousGetHandle());
                continue;
            }

            Volatile.Write(ref owner._controlWaiting, 1);
            if (!Add(arrivals, control) || !Take(release))
            {
                return;
            }
        }
    }

    private static bool Add(BlockingCollection<CursorOverlayControl> arrivals, CursorOverlayControl control)
    {
        try
        {
            arrivals.Add(control);
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static bool Take(BlockingCollection<ReleaseToken> release)
    {
        try
        {
            return release.TryTake(out _, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// One control from a connected client, or nothing when it wrote nothing legible inside its window. The
    /// connection is left open for the caller to close, so the decision to disconnect lives in one place.
    /// </summary>
    /// <remarks>
    /// Bytes accumulate across polls. This is a byte-mode pipe, so a payload the client wrote in one call can still
    /// arrive in two reads, and decoding whichever half turned up first threw away a control its sender then waited
    /// out its whole budget for. A prefix of JSON cannot decode, so retrying the decode on every poll can never
    /// accept a half-written control as a whole one.
    /// </remarks>
    private static CursorOverlayControl? ReadControl(SafeFileHandle handle)
    {
        long deadline = StopwatchFrom(PayloadWait);
        byte[] payload = [];
        while (true)
        {
            if (!PeekNamedPipe(handle.DangerousGetHandle(), IntPtr.Zero, 0, IntPtr.Zero, out uint available, IntPtr.Zero))
            {
                return null;
            }

            if (available > 0)
            {
                int room = Framing.MaxControlBytes - payload.Length;
                if (room == 0)
                {
                    return null;
                }

                byte[] chunk = new byte[room];
                int toRead = Math.Min((int)available, room);
                if (!ReadFile(handle.DangerousGetHandle(), chunk, (uint)toRead, out uint read, IntPtr.Zero) || read == 0)
                {
                    return null;
                }

                payload = [.. payload, .. chunk[..(int)read]];
                try
                {
                    CursorOverlayControl control = Framing.Decode(payload);
                    control.Validate();
                    return control;
                }
                catch (AdapterError)
                {
                }
            }

            if (Stopwatch.GetTimestamp() >= deadline)
            {
                return null;
            }

            Thread.Sleep(Poll);
        }
    }

    private static long StopwatchFrom(TimeSpan span) => Stopwatch.GetTimestamp() + (long)(span.TotalSeconds * Stopwatch.Frequency);

    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", EntryPoint = "CreateNamedPipeW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateNamedPipeW(
        char[] name,
        uint openMode,
        uint pipeMode,
        uint maxInstances,
        uint outBufferSize,
        uint inBufferSize,
        uint defaultTimeout,
        IntPtr securityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "ConnectNamedPipe", SetLastError = true)]
    private static extern int ConnectNamedPipe(IntPtr namedPipe, IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "DisconnectNamedPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DisconnectNamedPipe(IntPtr namedPipe);

    [DllImport("kernel32.dll", EntryPoint = "FlushFileBuffers", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(IntPtr file);

    [DllImport("kernel32.dll", EntryPoint = "PeekNamedPipe", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekNamedPipe(
        IntPtr namedPipe,
        IntPtr buffer,
        uint bufferSize,
        IntPtr bytesRead,
        out uint totalBytesAvailable,
        IntPtr bytesLeftThisMessage);

    [DllImport("kernel32.dll", EntryPoint = "ReadFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(
        IntPtr file,
        byte[] buffer,
        uint numberOfBytesToRead,
        out uint numberOfBytesRead,
        IntPtr overlapped);

    [DllImport("kernel32.dll", EntryPoint = "WriteFile", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        IntPtr file,
        byte[] buffer,
        uint numberOfBytesToWrite,
        out uint numberOfBytesWritten,
        IntPtr overlapped);
}

/// <summary>Why a renderer could not claim its pipe name.</summary>
internal enum ClaimErrorKind
{
    /// <summary>Another renderer already serves this session. Withdrawing here, before a window exists, is what keeps
    /// a duplicate from drawing.</summary>
    AlreadyServed,

    /// <summary>The claim failed for a reason the caller has to report.</summary>
    Failed,
}

/// <summary>The claim failure, carrying the kind the caller branches on and the error it reports for
/// <see cref="ClaimErrorKind.Failed"/>.</summary>
internal sealed class ClaimException : Exception
{
    private ClaimException(ClaimErrorKind kind, AdapterError? error)
    {
        Kind = kind;
        Error = error;
    }

    internal ClaimErrorKind Kind { get; }

    internal AdapterError? Error { get; }

    internal static ClaimException AlreadyServed() => new(ClaimErrorKind.AlreadyServed, null);

    internal static ClaimException Failed(AdapterError error) => new(ClaimErrorKind.Failed, error);
}

/// <summary>What one tick of the control loop found.</summary>
internal abstract record Accepted;

/// <summary>A control arrived and is ready to serve.</summary>
internal sealed record ControlAccepted(CursorOverlayControl Control) : Accepted;

/// <summary>Nothing arrived within the tick.</summary>
internal sealed record IdleAccepted : Accepted;

/// <summary>The accept worker stopped, so no further control will be served.</summary>
internal sealed record BrokenAccepted(AdapterError Error) : Accepted;

/// <summary>The token that hands the accept worker its rendezvous slot back.</summary>
internal readonly struct ReleaseToken;
