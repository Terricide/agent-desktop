using AgentDesktop.Core.Adapter;
using AgentDesktop.Core.Input;
using AgentDesktop.Core.Response;

namespace AgentDesktop.Cli;

/// <summary>Refuses a key combo the platform adapter reports as dangerous, unless the caller passed
/// `--force`. Whether a combo is dangerous is the adapter's decision; the gate enforces the verdict
/// and honors the override, so the calling agent always keeps control.</summary>
public static class KeyComboGate
{
    /// <summary>The refusal for a combo that must not be sent, or null when it may proceed.</summary>
    public static AdapterError? Refusal(KeyCombo combo, string raw, bool force, PlatformAdapter adapter)
    {
        if (force || !adapter.IsBlockedCombo(combo))
        {
            return null;
        }

        return new AdapterError(ErrorCode.PolicyDenied, $"Key combo '{raw}' is blocked for safety; pass --force to override")
            .WithSuggestion("Use an explicit mouse/focus command if physical interaction is intended")
            .WithDisposition(DeliverySemantics.NotDelivered());
    }
}
