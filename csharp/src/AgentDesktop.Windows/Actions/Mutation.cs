using AgentDesktop.Core.Response;
using AgentDesktop.Windows.Tree;

namespace AgentDesktop.Windows.Actions;

/// <summary>
/// Write-path classifier for UIA mutation failures. Every Windows write turns its failure
/// into a delivery outcome only through this table. The read-path classifiers are
/// unreachable from here by construction. Measured shapes this table matches: get_pattern
/// absent is sentinel 0; disabled SetValue is 0x80040200 (UIA_E_ELEMENTNOTENABLED); a killed
/// provider is 0x80040201 (UIA_E_ELEMENTNOTAVAILABLE); 0x80131509 (UIA_E_INVALIDOPERATION)
/// lands unclassified and uncertain by design (A19-2).
/// </summary>
internal static class Mutation
{
    internal const int ErrNone = 0;

    internal const int ErrNotFound = 1;

    internal const int UiaENotSupported = unchecked((int)0x8004_0204);

    internal const int UiaEElementNotEnabled = unchecked((int)0x8004_0200);

    internal const int UiaEElementNotAvailable = unchecked((int)0x8004_0201);

    internal const int UiaEInvalidOperation = unchecked((int)0x8013_1509);

    internal const int UiaETimeout = unchecked((int)0x8013_1505);

    internal const int EAccessDenied = unchecked((int)0x8007_0005);

    internal const int EInvalidArg = unchecked((int)0x8007_0057);

    internal const int RpcEServerFault = unchecked((int)0x8001_0105);

    internal const int RpcEDisconnected = unchecked((int)0x8001_0108);

    internal const int RpcSServerUnavailable = unchecked((int)0x8007_06BA);

    internal const int RpcSCallFailed = unchecked((int)0x8007_06BE);

    internal const string InspectSuggestion =
        "Inspect the target state with a fresh snapshot before deciding whether to retry.";

    internal const string RefreshSuggestion =
        "Refresh the snapshot and retry with the new element reference.";

    /// <summary>Maps a successful UIA write onto the delivered mutation outcome.</summary>
    internal static bool ClassifySuccess() => true;

    /// <summary>
    /// Classifies a failed UIA write into delivered, absent, or a structured error. True is never
    /// produced here; use <see cref="ClassifySuccess"/> for a clean write. False means the
    /// affordance is genuinely absent and the chain may fall through.
    /// </summary>
    internal static bool ClassifyMutation(string operation, string api, WriteFailure failure) => failure.Kind switch
    {
        WriteFailureKind.Hresult => ClassifyHresult(operation, api, failure.Value),
        _ => ClassifySentinel(operation, api, failure.Value),
    };

    /// <summary>
    /// Classifies a UIA write failure for chain fallthrough. Sentinel <see cref="ErrNone"/> and
    /// exhaustion map to absent; every other failure routes through <see cref="ClassifyMutation"/>.
    /// </summary>
    internal static bool ClassifyWrite(string operation, string api, WriteFailure failure)
    {
        if (failure.Kind == WriteFailureKind.Sentinel && failure.Value == ErrNone)
        {
            return false;
        }

        return ClassifyMutation(operation, api, failure);
    }

    private static bool ClassifyHresult(string operation, string api, int hresult)
    {
        if (hresult == UiaENotSupported)
        {
            return false;
        }

        if (hresult == EAccessDenied)
        {
            throw new AdapterError(ErrorCode.PermDenied, "Accessibility permission not granted")
                .WithSuggestion(
                    "Run agent-desktop permissions to see which permission is denied and how to grant it. On macOS, agent-desktop permissions --guide opens System Settings with a guide")
                .WithPlatformDetail(PlatformHresult(api, operation, hresult))
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        if (hresult == UiaEElementNotAvailable)
        {
            throw new AdapterError(
                    ErrorCode.StaleRef,
                    $"{operation} targeted an unavailable UI Automation element")
                .WithPlatformDetail(PlatformHresult(api, operation, hresult))
                .WithDisposition(DeliverySemantics.NotDelivered())
                .WithSuggestion(RefreshSuggestion);
        }

        if (hresult == EInvalidArg)
        {
            throw new AdapterError(
                    ErrorCode.InvalidArgs,
                    $"{operation} rejected an invalid UI Automation argument")
                .WithPlatformDetail(PlatformHresult(api, operation, hresult))
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        if (hresult == UiaEElementNotEnabled)
        {
            throw new AdapterError(
                    ErrorCode.ActionFailed,
                    $"{operation} targeted an element that is not enabled")
                .WithPlatformDetail(PlatformHresult(api, operation, hresult))
                .WithDisposition(DeliverySemantics.NotDelivered());
        }

        if (hresult is RpcEServerFault or RpcEDisconnected or RpcSServerUnavailable or RpcSCallFailed)
        {
            throw Uncertain(ErrorCode.AppUnresponsive, operation, api, hresult, "transport failure");
        }

        if (hresult == UiaETimeout)
        {
            throw Uncertain(ErrorCode.Timeout, operation, api, hresult, "UIA_E_TIMEOUT");
        }

        throw Uncertain(ErrorCode.ActionFailed, operation, api, hresult, "unclassified HRESULT");
    }

    private static bool ClassifySentinel(string operation, string api, int sentinel)
    {
        if (sentinel == ErrNone)
        {
            return false;
        }

        throw new AdapterError(
                ErrorCode.ActionFailed,
                $"{operation} returned unclassified sentinel; mutation outcome is uncertain")
            .WithPlatformDetail($"{api}({operation}) returned sentinel {sentinel}")
            .WithDisposition(DeliverySemantics.DeliveryUncertain())
            .WithSuggestion(InspectSuggestion);
    }

    private static AdapterError Uncertain(ErrorCode code, string operation, string api, int hresult, string label) =>
        new AdapterError(code, $"{operation} returned {label}; mutation outcome is uncertain")
            .WithPlatformDetail(PlatformHresult(api, operation, hresult))
            .WithDisposition(DeliverySemantics.DeliveryUncertain())
            .WithSuggestion(InspectSuggestion);

    private static string PlatformHresult(string api, string operation, int hresult) =>
        $"{api}({operation}) returned {UiaFailure.Detail(hresult)}";
}
