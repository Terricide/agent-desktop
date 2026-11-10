using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System;

/// <summary>
/// The one place a Win32 last-error code becomes a structured error. A call that fails reports the
/// code the OS left behind; the code decides the <c>ErrorCode</c> and the delivery disposition, so
/// a failure the caller cannot act on is never reported as a generic one, and a failure that
/// emitted nothing is stamped not-delivered.
/// </summary>
internal static class Win32Errors
{
    private const uint AccessDenied = 5;
    private const uint InvalidFunction = 1;
    private const uint InvalidParameter = 87;
    private const uint EnvironmentVariableNotSet = 1203;
    private const uint NoMoreItems = 259;
    private const uint Timeout = 1460;
    private const uint Cancelled = 1223;

    /// <summary>Names the failure the last Win32 call left behind, for the operation that asked for
    /// it. The code is carried as platform detail so a caller can read the exact failure the OS
    /// reported, and the disposition says what the operation did: a call that failed before it
    /// delivered anything is stamped not-delivered, and a call that timed out mid-operation is not.</summary>
    internal static AdapterError FromLastError(string context)
    {
        var code = (uint)Marshal.GetLastWin32Error();
        var error = new AdapterError(CodeFor(code), $"{context} failed")
            .WithPlatformDetail($"GetLastError={code}");

        return code == Timeout
            ? error.WithDisposition(DeliverySemantics.DeliveredUnverified())
            : error.WithDisposition(DeliverySemantics.NotDelivered());
    }

    /// <summary>Maps the codes a liveness or handle call can raise. An unlisted code is
    /// <c>INTERNAL</c>: the OS reported a failure this build does not name, and inventing a
    /// caller-facing code for it would send the caller down a recovery path that does not apply.</summary>
    private static ErrorCode CodeFor(uint code) => code switch
    {
        AccessDenied => ErrorCode.PermDenied,
        InvalidFunction => ErrorCode.InvalidArgs,
        InvalidParameter => ErrorCode.InvalidArgs,
        EnvironmentVariableNotSet => ErrorCode.InvalidArgs,
        NoMoreItems => ErrorCode.AppNotFound,
        Timeout => ErrorCode.Timeout,
        Cancelled => ErrorCode.ActionFailed,
        _ => ErrorCode.Internal,
    };
}
