using System.Runtime.InteropServices;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Windows.System.Runtime;

/// <summary>
/// The one place a COM status code is named. Both the permission probe and the UI Automation tree path
/// classify and format HRESULTs through <see cref="HresultRecord"/>, so a code's read disposition and its
/// caller-facing <see cref="ErrorCode"/> are the same fact, written once.
/// </summary>
internal static class HResult
{
    public const int SOk = 0;
    public const int ENoInterface = unchecked((int)0x80004002);
    public const int EPointer = unchecked((int)0x80004003);
    public const int EFail = unchecked((int)0x80004005);
    public const int EUnexpected = unchecked((int)0x8000FFFF);
    public const int EAccessDenied = unchecked((int)0x80070005);
    public const int EInvalidArg = unchecked((int)0x80070057);
    public const int CoENotInitialized = unchecked((int)0x800401F0);
    public const int RpcEServerFault = unchecked((int)0x80010105);
    public const int RpcEDisconnected = unchecked((int)0x80010108);
    public const int RpcSServerUnavailable = unchecked((int)0x800706BA);
    public const int RpcSCallFailed = unchecked((int)0x800706BE);
    public const int UiaEElementNotEnabled = unchecked((int)0x80040200);
    public const int UiaEElementNotAvailable = unchecked((int)0x80040201);
    public const int UiaENoClickablePoint = unchecked((int)0x80040202);
    public const int UiaEProxyAssemblyNotLoaded = unchecked((int)0x80040203);
    public const int UiaENotSupported = unchecked((int)0x80040204);
    public const int UiaETimeout = unchecked((int)0x80131505);
    public const int UiaEInvalidOperation = unchecked((int)0x80131509);
    public const int WincodecErrComponentNotFound = unchecked((int)0x88982F50);
    public const int WincodecErrBadImage = unchecked((int)0x88982F60);
    public const int WincodecErrUnsupportedPixelFormat = unchecked((int)0x88982F80);
    public const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);

    private const string ComUninitializedSuggestion =
        "Join the calling thread to the COM multithreaded apartment before observing the desktop";

    /// <summary>
    /// Names every HRESULT the read paths raise, exactly once. An unlisted code is
    /// <see cref="ReadDisposition.Terminal"/> with <see cref="ErrorCode.Internal"/>: the loop retries only
    /// what is marked retryable, so an unknown failure surfaces rather than being guessed.
    /// <para>
    /// <c>EUnexpected</c> is retryable on purpose. A provider whose window or host is tearing down answers a
    /// read with it while the target changes under the read, so the next poll finds the window gone or reads
    /// the settled provider. Every loop that consumes the retryable stamp is bounded by its caller's deadline.
    /// </para>
    /// <para>
    /// <c>SOk</c> is unlisted: a null interface out-param, the end-of-list signal, is read as exhaustion, never
    /// as a failure. The one path that reaches the default arm with <c>SOk</c> is client bootstrap, where
    /// <c>CoCreateInstance</c> reported success and returned a null interface; that is a broken class factory,
    /// and terminal is the honest answer.
    /// </para>
    /// <para>
    /// <c>UiaENoClickablePoint</c> is unlisted: the documented causes are view state a caller can change, not a
    /// provider lacking a property, and <c>GetClickablePoint</c> reports absence as <c>S_OK</c> with a
    /// <c>FALSE</c> out-param rather than as this error.
    /// </para>
    /// <para>
    /// <c>UiaEProxyAssemblyNotLoaded</c> is unlisted: it reports this process's UI Automation stack failing, not
    /// an answer about the target, and no retry changes it.
    /// </para>
    /// </summary>
    public static HresultRecord Record(int hresult) => hresult switch
    {
        EAccessDenied => new(ReadDisposition.Terminal, ErrorCode.PermDenied, null),
        CoENotInitialized => new(ReadDisposition.Terminal, ErrorCode.Internal, ComUninitializedSuggestion),
        EInvalidArg or EPointer => new(ReadDisposition.SettledAbsence, ErrorCode.InvalidArgs, null),
        UiaEElementNotAvailable => new(ReadDisposition.Unavailable, ErrorCode.StaleRef, null),
        UiaEElementNotEnabled => new(ReadDisposition.Terminal, ErrorCode.ActionFailed, null),
        UiaENotSupported => new(ReadDisposition.SettledAbsence, ErrorCode.ActionNotSupported, null),
        UiaETimeout => new(ReadDisposition.Retryable, ErrorCode.Timeout, null),
        UiaEInvalidOperation => new(ReadDisposition.Terminal, ErrorCode.ActionFailed, null),
        RpcEDisconnected or RpcEServerFault or RpcSServerUnavailable or RpcSCallFailed or EUnexpected
            => new(ReadDisposition.Retryable, ErrorCode.AppUnresponsive, null),
        WincodecErrBadImage or WincodecErrUnsupportedPixelFormat
            => new(ReadDisposition.Terminal, ErrorCode.InvalidArgs, null),
        WincodecErrComponentNotFound => new(ReadDisposition.Terminal, ErrorCode.Internal, null),
        DxgiErrorDeviceRemoved => new(ReadDisposition.Retryable, ErrorCode.ActionFailed, null),
        _ => new(ReadDisposition.Terminal, ErrorCode.Internal, null),
    };

    /// <summary>A one-line projection of <see cref="Record"/>.</summary>
    public static ReadDisposition ClassifyReadHresult(int hresult) => Record(hresult).Disposition;

    /// <summary>
    /// The typed channel pair the resolution loop and core's hydration read: a settled absence or terminal
    /// failure is complete and never retried; transport and a vanished element are incomplete and retryable.
    /// </summary>
    public static (bool Complete, bool Retryable) RetryDetails(ReadDisposition disposition) => disposition switch
    {
        ReadDisposition.SettledAbsence or ReadDisposition.Terminal => (true, false),
        ReadDisposition.Retryable or ReadDisposition.Unavailable => (false, true),
        _ => (true, false),
    };

    /// <summary>Renders an HRESULT for <c>platform_detail</c>: a code, and its symbol and meaning where known.</summary>
    public static string ComHresultDetail(int hresult)
    {
        var code = (uint)hresult;
        return ComHresultSymbol(hresult) is var (symbol, meaning)
            ? $"COM HRESULT 0x{code:X8} ({symbol}: {meaning})"
            : $"COM HRESULT 0x{code:X8}";
    }

    /// <summary>
    /// An HRESULT read through <see cref="Record"/> and rendered as the <see cref="AdapterError"/> its caller
    /// reports. <paramref name="subsystem"/> names the COM layer that raised it ("D3D", "WGC", "WIC", ...).
    /// </summary>
    public static AdapterError HresultError(string subsystem, int hresult, string context)
    {
        var record = Record(hresult);
        var error = new AdapterError(record.Code, $"{subsystem} could not {context}")
            .WithPlatformDetail(ComHresultDetail(hresult));
        return record.Suggestion is { } suggestion ? error.WithSuggestion(suggestion) : error;
    }

    /// <summary>Names the HRESULTs the COM paths can raise. An unlisted code is <c>null</c>, rendered as bare hex.</summary>
    public static (string Symbol, string Meaning)? ComHresultSymbol(int hresult) => hresult switch
    {
        EAccessDenied => ("E_ACCESSDENIED", "Access is denied"),
        ENoInterface => ("E_NOINTERFACE", "No such interface supported"),
        EPointer => ("E_POINTER", "Invalid pointer"),
        EFail => ("E_FAIL", "Unspecified failure"),
        EUnexpected => ("E_UNEXPECTED", "Catastrophic failure"),
        EInvalidArg => ("E_INVALIDARG", "One or more arguments are invalid"),
        CoENotInitialized => ("CO_E_NOTINITIALIZED", "COM has not been initialized"),
        RpcEServerFault => ("RPC_E_SERVERFAULT", "The server raised an exception"),
        RpcEDisconnected => ("RPC_E_DISCONNECTED", "The object invoked has disconnected"),
        RpcSServerUnavailable => ("RPC_S_SERVER_UNAVAILABLE", "The RPC server is unavailable"),
        RpcSCallFailed => ("RPC_S_CALL_FAILED", "The remote procedure call failed"),
        UiaEElementNotEnabled => ("UIA_E_ELEMENTNOTENABLED", "The element is not enabled"),
        UiaEElementNotAvailable => ("UIA_E_ELEMENTNOTAVAILABLE", "The element is not available"),
        UiaENoClickablePoint => ("UIA_E_NOCLICKABLEPOINT", "The element has no clickable point"),
        UiaEProxyAssemblyNotLoaded => ("UIA_E_PROXYASSEMBLYNOTLOADED", "The proxy assembly could not be loaded"),
        UiaENotSupported => ("UIA_E_NOTSUPPORTED", "The requested operation is unsupported"),
        UiaETimeout => ("UIA_E_TIMEOUT", "The operation timed out"),
        UiaEInvalidOperation => ("UIA_E_INVALIDOPERATION", "The operation is not valid"),
        WincodecErrComponentNotFound => ("WINCODEC_ERR_COMPONENTNOTFOUND", "The imaging component was not found"),
        WincodecErrBadImage => ("WINCODEC_ERR_BADIMAGE", "The image is unrecognized"),
        WincodecErrUnsupportedPixelFormat => ("WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT", "The bitmap pixel format is unsupported"),
        DxgiErrorDeviceRemoved => ("DXGI_ERROR_DEVICE_REMOVED", "The GPU device has been removed"),
        _ => null,
    };

    /// <summary>
    /// <c>HRESULT_FROM_WIN32</c>: the measured path for Win32 codes into the one HRESULT table (A21-8).
    /// </summary>
    public static int HresultFromWin32(uint error)
    {
        if (error == 0)
        {
            return 0;
        }

        if ((int)error < 0)
        {
            return unchecked((int)error);
        }

        return unchecked((int)((error & 0xFFFF) | 0x80070000u));
    }

    /// <summary>
    /// A Win32 error code rendered through the one HRESULT table. The caller supplies the <c>GetLastError</c>
    /// value, read immediately after the failing call (A21-8).
    /// </summary>
    public static AdapterError AdapterErrorFromWin32(uint error, string message)
    {
        var hresult = HresultFromWin32(error);
        var record = Record(hresult);
        var err = new AdapterError(record.Code, message).WithPlatformDetail(ComHresultDetail(hresult));
        return record.Suggestion is { } suggestion ? err.WithSuggestion(suggestion) : err;
    }

    /// <summary>
    /// Win32 <c>GetLastError</c> rendered through the one HRESULT table. The value is read here, so the
    /// failing call must be the last one this thread made (A21-8).
    /// </summary>
    public static AdapterError Win32LastError(string message) =>
        AdapterErrorFromWin32((uint)Marshal.GetLastPInvokeError(), message);
}
