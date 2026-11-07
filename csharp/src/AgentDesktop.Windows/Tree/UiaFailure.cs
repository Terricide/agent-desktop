using System.Text.Json.Nodes;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.Tree;

/// <summary>The error a failed UI Automation call surfaces, classified the way hresult_record
/// in crates/windows/src/system/runtime/hresult.rs classifies it. The message names what
/// failed and the platform detail carries the HRESULT, with its symbol and meaning where
/// com_hresult_symbol names one; neither carries target text. Every
/// error is stamped with the complete/retryable pair core's retry hint reads: a transport
/// failure or a vanished element is incomplete and retryable, anything else is complete and
/// never retried.</summary>
internal static class UiaFailure
{
    internal const int ElementNotAvailable = unchecked((int)0x8004_0201);

    private const uint ComNotInitialized = 0x8004_01F0;

    internal static AdapterError Error(int hresult, string context)
    {
        var (code, retryable) = Classify(hresult);
        var error = new AdapterError(code, $"UI Automation could not {context}")
            .WithPlatformDetail(hresult < 0 ? Detail(hresult) : "UI Automation returned no object")
            .WithDetails(new JsonObject { ["complete"] = !retryable, ["retryable"] = retryable });
        return unchecked((uint)hresult) == ComNotInitialized
            ? error.WithSuggestion("Join the calling thread to the COM multithreaded apartment before observing the desktop")
            : error;
    }

    /// <summary>The HRESULT rendered the way com_hresult_detail renders it. An unlisted code
    /// is a bare hexadecimal value rather than a guess.</summary>
    internal static string Detail(int hresult) => Symbol(hresult) is { } named
        ? $"COM HRESULT 0x{hresult:X8} ({named})"
        : $"COM HRESULT 0x{hresult:X8}";

    /// <summary>A success with no object is the client answering nothing, which no retry
    /// changes, so it classifies with the unnamed codes.</summary>
    internal static (ErrorCode Code, bool Retryable) Classify(int hresult) => unchecked((uint)hresult) switch
    {
        0x8007_0005 => (ErrorCode.PermDenied, false),
        ComNotInitialized => (ErrorCode.Internal, false),
        0x8007_0057 or 0x8000_4003 => (ErrorCode.InvalidArgs, false),
        0x8004_0201 => (ErrorCode.StaleRef, true),
        0x8004_0200 => (ErrorCode.ActionFailed, false),
        0x8004_0204 => (ErrorCode.ActionNotSupported, false),
        0x8013_1505 => (ErrorCode.Timeout, true),
        0x8013_1509 => (ErrorCode.ActionFailed, false),
        0x8001_0108 or 0x8001_0105 or 0x8007_06BA or 0x8007_06BE or 0x8000_FFFF => (ErrorCode.AppUnresponsive, true),
        _ => (ErrorCode.Internal, false),
    };

    private static string? Symbol(int hresult) => unchecked((uint)hresult) switch
    {
        0x8007_0005 => "E_ACCESSDENIED: Access is denied",
        0x8000_4002 => "E_NOINTERFACE: No such interface supported",
        0x8000_4003 => "E_POINTER: Invalid pointer",
        0x8000_4005 => "E_FAIL: Unspecified failure",
        0x8000_FFFF => "E_UNEXPECTED: Catastrophic failure",
        0x8007_0057 => "E_INVALIDARG: One or more arguments are invalid",
        ComNotInitialized => "CO_E_NOTINITIALIZED: COM has not been initialized",
        0x8001_0105 => "RPC_E_SERVERFAULT: The server raised an exception",
        0x8001_0108 => "RPC_E_DISCONNECTED: The object invoked has disconnected",
        0x8007_06BA => "RPC_S_SERVER_UNAVAILABLE: The RPC server is unavailable",
        0x8007_06BE => "RPC_S_CALL_FAILED: The remote procedure call failed",
        0x8004_0200 => "UIA_E_ELEMENTNOTENABLED: The element is not enabled",
        0x8004_0201 => "UIA_E_ELEMENTNOTAVAILABLE: The element is not available",
        0x8004_0202 => "UIA_E_NOCLICKABLEPOINT: The element has no clickable point",
        0x8004_0203 => "UIA_E_PROXYASSEMBLYNOTLOADED: The proxy assembly could not be loaded",
        0x8004_0204 => "UIA_E_NOTSUPPORTED: The requested operation is unsupported",
        0x8013_1505 => "UIA_E_TIMEOUT: The operation timed out",
        0x8013_1509 => "UIA_E_INVALIDOPERATION: The operation is not valid",
        _ => null,
    };
}
