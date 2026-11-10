using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;
using AgentDesktop.Windows.System.Runtime;

namespace AgentDesktop.Windows.Input;

/// <summary>UIPI elevation detection: a token-integrity comparison, never the `SendInput`
/// verdict. `GetTokenInformation(TokenIntegrityLevel)` reads the caller's own token and, where
/// a target process is named, that process's token; a target strictly higher than the caller
/// maps to `PERM_DENIED`. An integrity level this process cannot read is never asserted
/// same-integrity: a blocked write silently no-ops and the caller's re-read judges effect, so
/// an unreadable target proceeds best-effort rather than fabricating a verdict. Where both
/// levels are readable and same-or-lower, effect is judged by the leg's post-injection re-read,
/// not by this gate.</summary>
internal static class Elevation
{
    internal static void EnsureTargetIntegrityAllowsInput(int targetPid) =>
        EvaluateIntegrityGate(CurrentProcessIntegrityRid(), ProcessIntegrityRid(targetPid));

    /// <summary>The pure comparison: a target at or below the caller's mandatory-label RID is
    /// permitted, a strictly higher target is not. Takes RIDs rather than SIDs so the decision
    /// is testable against synthetic values with no process boundary involved.</summary>
    internal static bool IntegrityPermitsInput(uint callerRid, uint targetRid) => targetRid <= callerRid;

    internal static void EvaluateIntegrityGate(uint? callerRid, uint? targetRid)
    {
        if (callerRid is { } caller && targetRid is { } target && !IntegrityPermitsInput(caller, target))
        {
            throw ElevationDenied();
        }
    }

    internal static AdapterError ElevationDenied() =>
        new AdapterError(
            ErrorCode.PermDenied,
            "The target process runs at a higher integrity level than this process; UIPI blocks input across that boundary")
            .WithPlatformDetail("E_ACCESSDENIED: Access is denied")
            .WithDetails("{\"raw_input_emitted\":false}")
            .WithSuggestion(
                "Run agent-desktop at the target's integrity level, or act on an element that does not cross the UIPI boundary")
            .WithDisposition(DeliverySemantics.NotDelivered());

    /// <summary>The activation-worded refusal for a target strictly above this process. It names window
    /// activation, not input, because the blocked write is the foreground transition, and it claims no
    /// delivery: the activation never reached the handle.</summary>
    internal static AdapterError ActivationElevationDenied() =>
        new AdapterError(
            ErrorCode.PermDenied,
            "The target process runs at a higher integrity level than this process; UIPI blocks window activation across that boundary")
            .WithPlatformDetail(HResult.ComHresultDetail(HResult.EAccessDenied))
            .WithDetails("{\"physical_delivery_started\":false}")
            .WithSuggestion(
                "Run agent-desktop at the target's integrity level, or activate a window that does not cross the UIPI boundary")
            .WithDisposition(DeliverySemantics.NotDelivered());

    internal static uint? CurrentProcessIntegrityRid() => TokenIntegrityRid(GetCurrentProcess());

    /// <summary>Whether this process runs at the high mandatory label. A token whose integrity this
    /// process cannot read is not treated as elevated, so a probe that needs elevation is refused
    /// on an unreadable token rather than run on a guess.</summary>
    internal static bool IsElevated() =>
        CurrentProcessIntegrityRid() is { } rid && rid >= SecurityHighRid;

    internal static uint? ProcessIntegrityRid(int pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        var rid = TokenIntegrityRid(process);
        CloseHandle(process);
        return rid;
    }

    private static uint? TokenIntegrityRid(IntPtr process)
    {
        if (OpenProcessToken(process, TokenQuery, out var token) == 0)
        {
            return null;
        }

        var rid = ReadTokenIntegrityRid(token);
        CloseHandle(token);
        return rid;
    }

    private static uint? ReadTokenIntegrityRid(IntPtr token)
    {
        GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0u, out var required);
        if (required == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)required);
        try
        {
            if (GetTokenInformation(token, TokenIntegrityLevel, buffer, required, out _) == 0)
            {
                return null;
            }

            var label = Marshal.ReadIntPtr(buffer);
            return SidLastSubAuthority(label);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Extracts the last sub-authority, the mandatory-label RID, from a SID.</summary>
    internal static uint? SidLastSubAuthority(IntPtr sid)
    {
        if (sid == IntPtr.Zero)
        {
            return null;
        }

        var count = Marshal.ReadByte(sid, 1);
        if (count == 0)
        {
            return null;
        }

        var subAuthority = GetSidSubAuthority(sid, (uint)(count - 1));
        return subAuthority == IntPtr.Zero ? null : (uint)Marshal.ReadInt32(subAuthority);
    }

    private const uint TokenQuery = 0x0008;

    private const int TokenIntegrityLevel = 25;

    private const uint ProcessQueryLimitedInformation = 0x1000;

    private const uint SecurityHighRid = 0x3000;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern int CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll")]
    private static extern int OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll")]
    private static extern int GetTokenInformation(IntPtr token, int level, IntPtr buffer, uint length, out uint required);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
}
