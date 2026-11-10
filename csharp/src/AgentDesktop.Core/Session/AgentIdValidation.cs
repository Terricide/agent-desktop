using AgentDesktop.Core.Context;

namespace AgentDesktop.Core.Session;

/// <summary>
/// Agent ids are the same filesystem-safe names session ids are, so the same
/// rule decides them.
/// </summary>
public static class AgentIdValidation
{
    /// <summary>Whether <paramref name="id"/> is an agent id the session can keep.</summary>
    public static bool IsValid(string id) => SessionIdValidation.IsValid(id);
}
