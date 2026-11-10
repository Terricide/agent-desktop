using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.System.CursorOverlay;

/// <summary>
/// Who is on the other end of the control pipe.
/// </summary>
/// <remarks>
/// The pipe carries no security descriptor. This crate deliberately does not author one —
/// <c>private_file</c> records descriptor authoring and DACL validation as deliberately absent, after an
/// earlier attempt sank on <c>AceSize</c> handling, and a test pins the ACL/ACE symbol family out of it.
/// So the peer is authenticated instead, with the token APIs the crate already calls.
/// <para>
/// The check runs in both directions. A server that only checked its clients would still be a server a
/// local process could impersonate: the pipe's name is a deterministic function of the state root and
/// session id, so anything that wins the creation race receives every control the session sends and can
/// return the acknowledgement byte — which would make <c>data.rendered</c> report <c>true</c> while
/// nothing is drawn.
/// </para>
/// <para>
/// What this does not close is stated rather than implied: every agent host here is single-user, so a
/// same-user process running the real binary passes both checks. The security-descriptor decision is taken
/// against that known gap, not against a closed hole.
/// </para>
/// <para>
/// The two directions do not ask the same question. A client may legitimately run under any image — an FFI
/// host is one — so clients are authenticated by user alone. A server may not: the renderer is only ever
/// forked from this tool's own binary, so the server's image is checked too.
/// </para>
/// <para>
/// That server-side image check is against the file stem, so any same-user <c>agent-desktop.exe</c> at any
/// path passes it. Deliberate, and settled here so the question is not re-filed. The only strengthening
/// that is not theatre — demanding the server's full image path equal the client's own executable —
/// breaks two things that are not misuse. A debug build legitimately reaches a renderer a release build
/// started, which is the ordinary shape of working on this code; and under an FFI host the executable is
/// the host process, so the comparison could never pass at all. It also closes nothing: the adversary it
/// would exclude is a same-user process, and a same-user process can simply run the genuine binary, which
/// both draws and acknowledges. A check that costs the development workflow and the FFI host while an
/// attacker steps around it by using the real thing is not a check.
/// </para>
/// </remarks>
internal static class Peer
{
    private const int ProcessQueryLimitedInformation = 0x1000;
    private const int ProcessTerminate = 0x0001;
    private const uint TokenQuery = 0x0008;

    /// <summary>
    /// The client on this connection, resolved before its payload is read.
    /// </summary>
    /// <remarks>
    /// <c>ImpersonateNamedPipeClient</c> is the obvious call and cannot serve here: it adopts the security
    /// context of the last message read from the pipe, so with nothing read there is no context to assume
    /// and it fails. Resolving the process id instead keeps the no-payload-read property the impersonation
    /// route only appeared to offer.
    /// </remarks>
    internal static bool PeerIsThisUser(SafeFileHandle pipe)
    {
        if (Interop.GetNamedPipeClientProcessId(pipe, out uint processId) == 0)
        {
            return false;
        }

        IntPtr process = OpenForInspection(processId);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return RunsAsThisUser(process);
        }
        finally
        {
            Interop.CloseHandle(process);
        }
    }

    /// <summary>
    /// The server this client just connected to, checked before anything is written to it.
    /// </summary>
    /// <remarks>
    /// Both facts are read from the one handle: the user who runs it, and the image it runs. A same-user
    /// process that is not this tool's binary can still win the deterministic pipe name, and answering its
    /// acknowledgement byte would report a frame that was never drawn.
    /// </remarks>
    internal static bool ServerIsThisUser(SafeFileHandle pipe)
    {
        if (Interop.GetNamedPipeServerProcessId(pipe, out uint processId) == 0)
        {
            return false;
        }

        IntPtr process = OpenForInspection(processId);
        if (process == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return RunsAsThisUser(process) && RunsOurImage(process);
        }
        finally
        {
            Interop.CloseHandle(process);
        }
    }

    /// <summary>
    /// Ends the process serving this pipe, when it is one of ours, and reports whether it did.
    /// </summary>
    /// <remarks>
    /// This is not belt-and-braces behind a polite shutdown. A renderer on a generation this build no
    /// longer speaks exists precisely because the wire format changed, so it may be unable to decode the
    /// <c>Disable</c> that would have ended it politely — and what it leaves on screen is a topmost,
    /// click-through window with no taskbar entry that no control can address. Ending the process is what
    /// removes it: the OS destroys the layered window with its owner.
    /// <para>
    /// Guarded by the same two questions a client asks of any server it found, asked once, of the single
    /// handle the terminate is issued on. Resolving the id twice would leave a window in which it could be
    /// reused by an unrelated process between the check and the kill. The caller reaches here only for a
    /// name it derived for this exact session and a generation it has retired, so nothing else is in range
    /// to begin with.
    /// </para>
    /// </remarks>
    internal static bool TerminatePipeServer(SafeFileHandle pipe)
    {
        if (Interop.GetNamedPipeServerProcessId(pipe, out uint processId) == 0
            || processId == Interop.GetCurrentProcessId())
        {
            return false;
        }

        IntPtr handle = Interop.OpenProcess(ProcessQueryLimitedInformation | ProcessTerminate, false, processId);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if (!RunsAsThisUser(handle) || !RunsOurImage(handle))
            {
                return false;
            }

            return Interop.TerminateProcess(handle, 0) != 0;
        }
        finally
        {
            Interop.CloseHandle(handle);
        }
    }

    private static IntPtr OpenForInspection(uint processId) =>
        Interop.OpenProcess(ProcessQueryLimitedInformation, false, processId);

    private static bool RunsAsThisUser(IntPtr process)
    {
        SidBuffer? theirs = UserSidOf(process);
        SidBuffer? ours = OwnUserSid();
        return theirs is not null && ours is not null && ours.Matches(theirs);
    }

    private static bool RunsOurImage(IntPtr process) =>
        ImagePathOf(process) is string path && ImageIdentity.IsAgentDesktopImage(path);

    /// <summary>
    /// A buffer sized for the longest path the API can answer with. A short buffer fails with
    /// <c>ERROR_INSUFFICIENT_BUFFER</c>, which reads here as "not our renderer" — so an install path
    /// longer than the guess would refuse every control and take the overlay out on that machine alone.
    /// </summary>
    private const int MaxExtendedPath = 32768;

    /// <summary>
    /// Asked with no flags, so the answer is a Win32 path whose last component is a file name, rather
    /// than the <c>\Device\...</c> form.
    /// </summary>
    private static string? ImagePathOf(IntPtr process)
    {
        StringBuilder image = new(MaxExtendedPath);
        int size = MaxExtendedPath;
        if (Interop.QueryFullProcessImageNameW(process, 0, image, ref size) == 0 || size == 0)
        {
            return null;
        }

        return image.ToString(0, size);
    }

    private static SidBuffer? OwnUserSid()
    {
        try
        {
            return TokenSid.ReadTokenUserSid(Runtime.TokenSource.CurrentProcess);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SidBuffer? UserSidOf(IntPtr process)
    {
        if (Interop.OpenProcessToken(process, TokenQuery, out IntPtr token) == 0)
        {
            return null;
        }

        try
        {
            return TokenSid.ReadTokenUserSid(new Runtime.TokenSource(token));
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Interop.CloseHandle(token);
        }
    }
}
